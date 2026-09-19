using System.Collections.Concurrent;
using System.Diagnostics;
using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Commands;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Editors.Audio.Shared.Wwise.Engine.Timing;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Wwise.Engine.Rendering
{
    // Turns whatever the transport says is sounding into blocks of audio, and publishes what each
    // output frame was carrying so the animation can follow it.
    //
    // When a sound happens is the transport's; what it sounds like is this. The block is cut short
    // wherever the transport says something is due, so firing a cue — which runs container
    // resolution right here, inside the render callback — never moves an onset off its frame.
    internal sealed class AudioRenderer
    {
        // Rendering runs in fixed blocks so every stage after the voices consumes and produces the
        // same buffer shape, which is the precondition for filters, bus effects and a limiter.
        private const int MaximumBlockFrames = 512;

        // How many commands one block will apply before getting on with rendering.
        private const int MaximumCommandsPerBlock = 64;

        private readonly ConcurrentQueue<EngineCommand> _pendingCommands = new();
        private readonly EngineTelemetry _telemetry = new();
        private readonly VoicePool _voices;
        private readonly Transport _transport;
        private readonly Bus _masterBus = new(MaximumBlockFrames);
        private BusGraph _busGraph;
        private readonly Limiter _masterLimiter = new();
        private readonly OutputTimelineLedger _outputTimelineLedger = new();

        // Where a SetSwitch or SetState action leaves what it changed, for the control thread to
        // pick up: the registry it moves is control-thread data.
        private readonly PendingTransitionQueue _pendingTransitions = new();
        private long _completedTransportIdentifier;
        private long _completedOutputFrame = -1;
        private long _nextCommandSequence;
        private long _lastAppliedCommandSequence;
        private long _renderedOutputFrameCount;

        public long PlaybackPositionFrames => _transport.TimelineFrame;
        public TransportId CompletedTransportId => new(Interlocked.Read(ref _completedTransportIdentifier));
        public long CompletedOutputFrame => Interlocked.Read(ref _completedOutputFrame);
        public long LastAppliedCommandSequence => Interlocked.Read(ref _lastAppliedCommandSequence);
        public long RenderedOutputFrameCount => Interlocked.Read(ref _renderedOutputFrameCount);

        // What the engine did that nobody asked it to do, and how close to its limits it ran.
        // Counted here, said out loud by the control side, which is what keeps formatting off the
        // output callback.
        public EngineTelemetry Telemetry => _telemetry;

        public AudioRenderer(int maximumVoices = 32, int maximumScheduledEvents = 512)
        {
            _voices = new VoicePool(maximumVoices, _telemetry);
            _transport = new Transport(_voices, _telemetry, _pendingTransitions, maximumScheduledEvents, maximumVoices);
            _busGraph = BusGraph.Create([], MaximumBlockFrames, _telemetry);
        }

        public void ConfigureBuses(IReadOnlyList<HircItem> masterMixerNodes)
            => SubmitCommand(EngineCommand.ConfigureBuses(BusGraph.Create(masterMixerNodes, MaximumBlockFrames, _telemetry)));

        // Drained by the control thread on the poll it already runs.
        internal bool TryTakePendingTransition(out ResolvedTransition transition)
            => _pendingTransitions.TryTake(out transition);

        public long SubmitCommand(EngineCommand engineCommand)
        {
            var commandSequence = Interlocked.Increment(ref _nextCommandSequence);
            _pendingCommands.Enqueue(engineCommand.WithSequence(commandSequence));
            return commandSequence;
        }

        // Unbounded, unlike the drain inside a block: this runs on the control thread while the
        // output is stopped, where taking as long as it takes costs nothing.
        internal void ProcessPendingCommandsWithoutRendering()
            => ApplyPendingCommands(int.MaxValue, RenderedOutputFrameCount);

        internal bool TryGetTimelineFrame(long absoluteOutputFrame, TransportId transportId, out long timelineFrame)
        {
            return _outputTimelineLedger.TryGetTimelineFrameForOutputFrame(absoluteOutputFrame, transportId, out timelineFrame);
        }

        internal bool TryGetPostPosition(PlayingId playingId, long? absoluteOutputFrame, out long mixFrame)
        {
            return _voices.TryGetPosition(playingId, absoluteOutputFrame, out mixFrame);
        }

        public int Render(float[] destinationBuffer, int destinationOffset, int requestedSampleCount)
        {
            Array.Clear(destinationBuffer, destinationOffset, requestedSampleCount);

            var requestedFrameCount = requestedSampleCount / PlaybackFormat.ChannelCount;
            var firstAbsoluteOutputFrame = Interlocked.Read(ref _renderedOutputFrameCount);
            var renderStartTimestamp = Stopwatch.GetTimestamp();
            var renderedFrameCount = 0;
            while (renderedFrameCount < requestedFrameCount)
            {
                ApplyPendingCommands(MaximumCommandsPerBlock, firstAbsoluteOutputFrame + renderedFrameCount);
                renderedFrameCount += RenderBlock(
                    destinationBuffer,
                    destinationOffset + renderedFrameCount * PlaybackFormat.ChannelCount,
                    firstAbsoluteOutputFrame + renderedFrameCount,
                    Math.Min(MaximumBlockFrames, requestedFrameCount - renderedFrameCount));
            }

            Interlocked.Add(ref _renderedOutputFrameCount, requestedFrameCount);

            // Taking longer to render a buffer than the buffer lasts is the one measurement that
            // catches the cause of a glitch rather than its symptom, so it is taken here, on the
            // callback whose deadline it is.
            if (Stopwatch.GetElapsedTime(renderStartTimestamp) > PlaybackTime.FromFrames(requestedFrameCount))
                _telemetry.CountBlockOverrun();
            return requestedSampleCount;
        }

        // Returns the frames actually rendered, which is fewer than asked for whenever the block
        // had to be cut short so a cue could start on its own frame.
        private int RenderBlock(
            float[] destinationBuffer,
            int destinationSampleOffset,
            long firstAbsoluteOutputFrame,
            int maximumFrameCount)
        {
            _voices.StartDueContinuations(_transport.Id, _transport.ShouldMixTimelineVoices, firstAbsoluteOutputFrame);
            if (_transport.IsPlaying)
                _transport.StartDueEvents(firstAbsoluteOutputFrame);
            var blockFrameCount = _transport.IsPlaying
                ? _transport.LimitBlockFrames(maximumFrameCount)
                : maximumFrameCount;
            blockFrameCount = Math.Min(
                blockFrameCount,
                _voices.FramesUntilContinuation(_transport.Id, _transport.ShouldMixTimelineVoices));
            var blockFirstTimelineFrame = _transport.TimelineFrame;
            _transport.PrepareEndFade(blockFrameCount);

            _masterBus.Clear(blockFrameCount);
            _busGraph.Clear(blockFrameCount);
            _voices.MixBlock(
                _busGraph,
                _transport.Id,
                _transport.ShouldMixTimelineVoices,
                firstAbsoluteOutputFrame,
                blockFrameCount);
            _busGraph.MixTo(_masterBus, _transport.Gain, blockFrameCount, _telemetry);
            _telemetry.RecordMasterPeakLevel(_masterLimiter.Process(_masterBus.Samples, blockFrameCount));
            _masterBus.MixInto(destinationBuffer, destinationSampleOffset, blockFrameCount);
            _voices.AdvanceContinuations(_transport.Id, _transport.ShouldMixTimelineVoices, blockFrameCount);

            var transportAdvance = _transport.IsPlaying
                ? _transport.Advance(blockFrameCount)
                : TransportAdvance.Running(advancingFrameCount: 0);
            PublishTimelineFrames(
                firstAbsoluteOutputFrame,
                blockFirstTimelineFrame,
                blockFrameCount,
                transportAdvance.AdvancingFrameCount);
            if (transportAdvance.HasCompleted)
                CompletePlayback(firstAbsoluteOutputFrame + transportAdvance.AdvancingFrameCount);
            else if (_transport.TryFinishCompletionTail())
                CompletePlayback(firstAbsoluteOutputFrame + blockFrameCount);

            _transport.ApplyPendingActionIfSilent();
            return blockFrameCount;
        }

        // Published a frame at a time even though rendering is blocked, because this is what the
        // animation reads its position from and it is why cue onsets land within ±0.02 ms. Frames
        // past the point the audio ran out hold the frame it ran out on.
        private void PublishTimelineFrames(
            long firstAbsoluteOutputFrame,
            long firstTimelineFrame,
            int frameCount,
            int advancingFrameCount)
        {
            var transportId = _transport.Id;
            for (var frameOffset = 0; frameOffset < frameCount; frameOffset++)
                _outputTimelineLedger.PublishFrameMapping(
                    firstAbsoluteOutputFrame + frameOffset,
                    transportId,
                    firstTimelineFrame + Math.Min(frameOffset, advancingFrameCount));
        }

        // Bounded, because this runs inside the render callback: a control thread posting faster
        // than commands can be applied would otherwise hold the callback in here indefinitely, and
        // a late buffer is not recoverable. Whatever is left waits for the next block.
        private void ApplyPendingCommands(int maximumCommandCount, long absoluteOutputFrame)
        {
            for (var appliedCommandCount = 0; appliedCommandCount < maximumCommandCount; appliedCommandCount++)
            {
                if (!_pendingCommands.TryDequeue(out var engineCommand))
                    return;

                ApplyCommand(engineCommand, absoluteOutputFrame);
                Interlocked.Exchange(ref _lastAppliedCommandSequence, engineCommand.CommandSequence);
            }
        }

        private void ApplyCommand(EngineCommand engineCommand, long absoluteOutputFrame)
        {
            switch (engineCommand.CommandType)
            {
                case EngineCommandType.ConfigureBuses:
                    _busGraph = engineCommand.BusGraph;
                    break;

                case EngineCommandType.PlayMedia:
                    _transport.PlayMedia(engineCommand, absoluteOutputFrame);
                    break;

                case EngineCommandType.PostEvent:
                    _transport.PostEvent(engineCommand, absoluteOutputFrame);
                    break;

                case EngineCommandType.RetargetPlayingId:
                    _transport.RetargetPlayingId(engineCommand);
                    break;

                case EngineCommandType.CreateTimeline:
                    ResetCompletion();
                    _transport.CreateTimeline(
                        engineCommand.TransportId,
                        engineCommand.TimelineDurationFrames,
                        engineCommand.ShouldLoop);
                    break;

                case EngineCommandType.ScheduleEvent:
                    _transport.ScheduleEvent(engineCommand, absoluteOutputFrame);
                    break;

                case EngineCommandType.Start:
                    if (engineCommand.TransportId == _transport.Id)
                    {
                        ResetCompletion();
                        _transport.Start(engineCommand.TargetFrame, absoluteOutputFrame);
                    }
                    break;

                case EngineCommandType.PauseTimeline:
                    if (engineCommand.TransportId == _transport.Id)
                        _transport.Pause();
                    break;

                case EngineCommandType.ResumeTimeline:
                    if (engineCommand.TransportId == _transport.Id)
                        _transport.Resume(engineCommand.TargetFrame);
                    break;

                case EngineCommandType.StopTimeline:
                    if (engineCommand.TransportId == _transport.Id)
                        _transport.Stop();
                    break;

                case EngineCommandType.SeekTimeline:
                    if (engineCommand.TransportId == _transport.Id)
                    {
                        _transport.Seek(engineCommand.TargetFrame, absoluteOutputFrame);
                    }
                    break;

                case EngineCommandType.PausePlayingId:
                    _voices.PausePlayingId(engineCommand.PlayingId);
                    break;

                case EngineCommandType.ResumePlayingId:
                    _voices.ResumePlayingId(engineCommand.PlayingId, engineCommand.TargetFrame);
                    break;

                case EngineCommandType.SeekPlayingId:
                    _voices.SeekPlayingId(engineCommand.PlayingId, engineCommand.TargetFrame);
                    break;

                case EngineCommandType.StopPlayingId:
                    _transport.StopPlayingId(engineCommand.PlayingId);
                    break;
            }
        }

        private void ResetCompletion()
        {
            Interlocked.Exchange(ref _completedTransportIdentifier, 0);
            Interlocked.Exchange(ref _completedOutputFrame, -1);
        }

        private void CompletePlayback(long completedOutputFrame)
        {
            Interlocked.Exchange(ref _completedOutputFrame, completedOutputFrame);
            Interlocked.Exchange(ref _completedTransportIdentifier, _transport.Id.Value);
        }
    }
}
