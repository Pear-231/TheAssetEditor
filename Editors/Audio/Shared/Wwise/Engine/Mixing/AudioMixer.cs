using System.Collections.Concurrent;
using Editors.Audio.Shared.Wwise.Engine.Scheduling;
using Editors.Audio.Shared.Wwise.Engine.Timing;
using NAudio.Wave;

namespace Editors.Audio.Shared.Wwise.Engine.Mixing
{
    internal sealed class AudioMixer : ISampleProvider
    {
        private readonly ConcurrentQueue<MixerCommand> _pendingCommands = new();
        private readonly VoicePool _voices;
        private readonly VoiceScheduler _voiceScheduler;
        private readonly float[] _headroomByActiveVoiceCount;
        private readonly OutputTimelineLedger _outputTimelineLedger = new();
        private readonly ConcurrentQueue<VoiceCompletion> _completedVoices = new();
        private long _timelineFrame;
        private long _timelineDurationFrames;
        private long _playbackGeneration;
        private bool _hasTimeline;
        private bool _isTimelineLooping;
        private bool _isPlaying;
        private long _completedPlaybackGeneration;
        private long _completedOutputFrame = -1;
        private long _nextCommandSequence;
        private long _lastAppliedCommandSequence;
        private long _renderedOutputFrameCount;
        private bool _isSilenceForced;

        public WaveFormat WaveFormat => PlaybackFormat.WaveFormat;
        public long PlaybackPositionFrames => Interlocked.Read(ref _timelineFrame);
        public long CompletedPlaybackGeneration => Interlocked.Read(ref _completedPlaybackGeneration);
        public long CompletedOutputFrame => Interlocked.Read(ref _completedOutputFrame);
        public long LastAppliedCommandSequence => Interlocked.Read(ref _lastAppliedCommandSequence);
        public long RenderedOutputFrameCount => Interlocked.Read(ref _renderedOutputFrameCount);

        public AudioMixer(int maximumVoices = 32, int maximumScheduledVoices = 512)
        {
            _voices = new VoicePool(maximumVoices);
            _voiceScheduler = new VoiceScheduler(maximumScheduledVoices);
            _headroomByActiveVoiceCount = new float[maximumVoices + 1];
            for (var voiceCount = 1; voiceCount <= maximumVoices; voiceCount++)
                _headroomByActiveVoiceCount[voiceCount] = 0.8f / MathF.Sqrt(voiceCount);
        }
        
        public long SubmitCommand(MixerCommand mixerCommand)
        {
            var commandSequence = Interlocked.Increment(ref _nextCommandSequence);
            _pendingCommands.Enqueue(mixerCommand.WithSequence(commandSequence));
            return commandSequence;
        }

        internal void ProcessPendingCommandsWithoutRendering() => ApplyPendingCommands();

        internal void SilenceImmediately() => Volatile.Write(ref _isSilenceForced, true);

        internal bool HasAudibleContentOtherThan(long voiceIdentifier, long playbackGeneration)
        {
            return Volatile.Read(ref _hasTimeline)
                || _voices.HasActiveVoiceOtherThan(voiceIdentifier, playbackGeneration)
                || _voiceScheduler.HasScheduledVoiceOtherThan(voiceIdentifier, playbackGeneration);
        }

        internal bool TryGetTimelineFrame(long absoluteOutputFrame, long playbackGeneration, out long timelineFrame)
        {
            return _outputTimelineLedger.TryGetTimelineFrameForOutputFrame(absoluteOutputFrame, playbackGeneration, out timelineFrame);
        }

        internal bool TryPeekCompletedVoice(out VoiceCompletion completedVoice)
        {
            return _completedVoices.TryPeek(out completedVoice);
        }

        internal bool TryDequeueCompletedVoice(out VoiceCompletion completedVoice)
        {
            return _completedVoices.TryDequeue(out completedVoice);
        }

        internal bool TryGetVoicePosition(VoiceHandle voiceHandle, long? absoluteOutputFrame, out long pcmFrame)
        {
            return _voices.TryGetPosition(voiceHandle, absoluteOutputFrame, out pcmFrame);
        }

        public int Read(float[] destinationBuffer, int destinationOffset, int requestedSampleCount)
        {
            Array.Clear(destinationBuffer, destinationOffset, requestedSampleCount);
            ApplyPendingCommands();

            var requestedFrameCount = requestedSampleCount / PlaybackFormat.ChannelCount;
            var firstAbsoluteOutputFrame = Interlocked.Read(ref _renderedOutputFrameCount);
            if (Volatile.Read(ref _isSilenceForced))
            {
                for (var outputFrameOffset = 0; outputFrameOffset < requestedFrameCount; outputFrameOffset++)
                    PublishOutputTimelineFrame(firstAbsoluteOutputFrame + outputFrameOffset);
                Interlocked.Add(ref _renderedOutputFrameCount, requestedFrameCount);
                return requestedSampleCount;
            }

            for (var outputFrameOffset = 0; outputFrameOffset < requestedFrameCount; outputFrameOffset++)
            {
                var absoluteOutputFrame = firstAbsoluteOutputFrame + outputFrameOffset;
                PublishOutputTimelineFrame(absoluteOutputFrame);
                if (!_isPlaying)
                    continue;

                if (_hasTimeline)
                    _voiceScheduler.ActivateVoicesAtTimelineFrame(_playbackGeneration, _timelineFrame, _voices);

                var activeVoiceCount = _voices.MixNextFrame(
                    out var mixedLeftSample,
                    out var mixedRightSample,
                    absoluteOutputFrame,
                    _completedVoices);
                if (activeVoiceCount > 0)
                {
                    var headroomMultiplier = _headroomByActiveVoiceCount[activeVoiceCount];
                    var destinationSampleIndex = destinationOffset + outputFrameOffset * PlaybackFormat.ChannelCount;
                    destinationBuffer[destinationSampleIndex] = Math.Clamp(
                        mixedLeftSample * headroomMultiplier,
                        -1f,
                        1f);
                    destinationBuffer[destinationSampleIndex + 1] = Math.Clamp(
                        mixedRightSample * headroomMultiplier,
                        -1f,
                        1f);
                }

                if (_hasTimeline)
                    AdvanceTimeline(absoluteOutputFrame);
                else if (activeVoiceCount == 0)
                    CompletePlayback(absoluteOutputFrame);
                else
                    _timelineFrame++;
            }

            Interlocked.Add(ref _renderedOutputFrameCount, requestedFrameCount);
            return requestedSampleCount;
        }

        private void PublishOutputTimelineFrame(long absoluteOutputFrame)
            => _outputTimelineLedger.PublishFrameMapping(
                absoluteOutputFrame,
                _playbackGeneration,
                _timelineFrame);

        private void ApplyPendingCommands()
        {
            while (_pendingCommands.TryDequeue(out var mixerCommand))
            {
                ApplyCommand(mixerCommand);
                Interlocked.Exchange(ref _lastAppliedCommandSequence, mixerCommand.CommandSequence);
            }
        }

        private void ApplyCommand(MixerCommand mixerCommand)
        {
            switch (mixerCommand.CommandType)
            {
                case MixerCommandType.Play:
                    Volatile.Write(ref _isSilenceForced, false);
                    ResetCompletion();
                    if (!_isPlaying && !_hasTimeline)
                    {
                        _playbackGeneration = mixerCommand.PlaybackGeneration;
                        _timelineFrame = mixerCommand.TargetFrame;
                    }
                    _voices.TryActivate(
                        mixerCommand.VoiceIdentifier,
                        mixerCommand.PlaybackGeneration,
                        mixerCommand.PcmData!,
                        mixerCommand.TargetFrame,
                        mixerCommand.ShouldLoop,
                        isScheduledVoice: false,
                        timelineStartFrame: _timelineFrame);
                    _isPlaying = true;
                    break;

                case MixerCommandType.CreateTimeline:
                    ResetCompletion();
                    ClearCompletedVoices();
                    _voices.DeactivateAll();
                    _voiceScheduler.ClearScheduledVoices();
                    _playbackGeneration = mixerCommand.PlaybackGeneration;
                    _timelineFrame = 0;
                    _timelineDurationFrames = mixerCommand.TimelineDurationFrames;
                    _isTimelineLooping = mixerCommand.ShouldLoop;
                    _hasTimeline = true;
                    _isPlaying = false;
                    break;

                case MixerCommandType.Schedule:
                    if (mixerCommand.PlaybackGeneration == _playbackGeneration)
                        _voiceScheduler.AddScheduledVoice(mixerCommand);
                    break;

                case MixerCommandType.Start:
                    if (mixerCommand.PlaybackGeneration == _playbackGeneration)
                    {
                        Volatile.Write(ref _isSilenceForced, false);
                        ResetCompletion();
                        SeekInternal(mixerCommand.TargetFrame);
                        _isPlaying = true;
                    }
                    break;

                case MixerCommandType.Pause:
                    Volatile.Write(ref _isSilenceForced, true);
                    _isPlaying = false;
                    break;

                case MixerCommandType.Resume:
                    Volatile.Write(ref _isSilenceForced, false);
                    _isPlaying = true;
                    break;

                case MixerCommandType.Stop:
                    Volatile.Write(ref _isSilenceForced, true);
                    _voices.DeactivateAll();
                    _voiceScheduler.ClearScheduledVoices();
                    _hasTimeline = false;
                    _timelineFrame = 0;
                    _isPlaying = false;
                    ClearCompletedVoices();
                    break;

                case MixerCommandType.Seek:
                    if (mixerCommand.PlaybackGeneration == _playbackGeneration)
                        SeekInternal(mixerCommand.TargetFrame);
                    break;

                case MixerCommandType.CancelVoice:
                    _voices.CancelVoice(mixerCommand.VoiceIdentifier, mixerCommand.PlaybackGeneration);
                    _voiceScheduler.CancelScheduledVoice(
                        mixerCommand.VoiceIdentifier,
                        mixerCommand.PlaybackGeneration);
                    break;
            }
        }

        private void AdvanceTimeline(long absoluteOutputFrame)
        {
            _timelineFrame++;
            if (_timelineDurationFrames <= 0 || _timelineFrame < _timelineDurationFrames)
                return;

            if (_isTimelineLooping)
            {
                _timelineFrame = 0;
                return;
            }

            _voices.DeactivateAll();
            _voiceScheduler.ClearScheduledVoices();
            _isPlaying = false;
            _hasTimeline = false;
            CompletePlayback(absoluteOutputFrame + 1);
        }

        private void SeekInternal(long targetFrame)
        {
            ClearCompletedVoices();
            if (_hasTimeline)
            {
                _timelineFrame = Math.Clamp(targetFrame, 0, Math.Max(0, _timelineDurationFrames));
                _voices.DeactivateScheduledVoices();
                _voiceScheduler.ReconstructVoicesAtTimelineFrame(
                    _playbackGeneration,
                    _timelineFrame,
                    _voices);
                return;
            }

            _timelineFrame = Math.Max(0, targetFrame);
            _voices.SeekImmediateVoices(_playbackGeneration, _timelineFrame);
        }

        private void ResetCompletion()
        {
            Interlocked.Exchange(ref _completedPlaybackGeneration, 0);
            Interlocked.Exchange(ref _completedOutputFrame, -1);
        }

        private void ClearCompletedVoices()
        {
            while (_completedVoices.TryDequeue(out _))
            {
            }
        }

        private void CompletePlayback(long completedOutputFrame)
        {
            _isPlaying = false;
            Interlocked.Exchange(ref _completedOutputFrame, completedOutputFrame);
            Interlocked.Exchange(ref _completedPlaybackGeneration, _playbackGeneration);
        }
    }
}
