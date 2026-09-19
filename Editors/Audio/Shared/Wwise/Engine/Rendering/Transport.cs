using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Commands;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Scheduling;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;

namespace Editors.Audio.Shared.Wwise.Engine.Rendering
{
    // Where playback has got to, what it does when it reaches the ends, and the queue of events
    // waiting for their frame on the way. The renderer asks it how far it may run and hands back
    // what it rendered; everything about when a sound happens is decided here.
    //
    // This is only the scheduled timeline. Immediate posts are independent voice-owned playbacks;
    // they share the renderer and mix but never claim this clock or its controls.
    //
    // Audio thread only. Every method runs inside the render callback, either while a command is
    // applied or while a block is rendered, and the one field the control thread reads is read
    // through Interlocked.
    internal sealed class Transport
    {
        private readonly VoicePool _voices;
        private readonly EventScheduler _eventScheduler;
        private readonly EngineTelemetry _telemetry;

        // Shared with the scheduler rather than owned by it, because direct media resolves its
        // parameters here: the point of the seam is that nothing gets a voice without going
        // through it, including the entry point that holds no hierarchy at all.
        private readonly ParameterResolver _parameterResolver = new();

        // Pause and stop land once the output has ramped down rather than the moment the command is
        // applied, so the transport freezes on a frame that has actually been heard.
        private readonly GainRamp _gain = new();
        private TransportAction _actionAtSilence;

        private TransportId _transportId;
        private long _timelineFrame;
        private long _timelineDurationFrames;
        private bool _hasTimeline;
        private bool _isLooping;
        private bool _isPlaying;
        private bool _isCompleting;

        public Transport(
            VoicePool voices,
            EngineTelemetry telemetry,
            PendingTransitionQueue pendingTransitions,
            int maximumScheduledEvents,
            int maximumVoices)
        {
            _voices = voices;
            _telemetry = telemetry;
            _eventScheduler = new EventScheduler(voices, telemetry, _parameterResolver, pendingTransitions, maximumScheduledEvents, maximumVoices);
        }

        public TransportId Id => _transportId;
        public bool IsPlaying => _isPlaying;
        public bool ShouldMixTimelineVoices => _isPlaying || _isCompleting;
        public long TimelineFrame => Interlocked.Read(ref _timelineFrame);
        public GainRamp Gain => _gain;

        // A new timeline replaces whatever the transport was doing: everything the old one started
        // stops, and nothing it had scheduled survives it.
        public void CreateTimeline(TransportId transportId, long timelineDurationFrames, bool shouldLoop)
        {
            Begin();
            _voices.DeactivateScheduledVoices();
            _eventScheduler.ClearScheduledEvents();
            _transportId = transportId;
            _timelineFrame = 0;
            _timelineDurationFrames = timelineDurationFrames;
            _isLooping = shouldLoop;
            _hasTimeline = true;
            _isPlaying = false;
            _isCompleting = false;
        }

        // Opens at full gain rather than ramping in: the output was silent, and ramping here would
        // soften the attack of any cue sitting on the first frame. A cue the start lands part way
        // through gets its own ramp inside the voice.
        public void Start(long timelineFrame, long absoluteOutputFrame)
        {
            Begin();

            // Starting is not seeking. A seek cross-fades one sounding position into another and
            // wants the longer ramp for it; a start has silence behind it, so its cues ramp in only
            // far enough to not click.
            Seek(timelineFrame, absoluteOutputFrame, GainRamp.DeclickFrames);
            _isPlaying = true;
        }

        public void Pause()
        {
            _actionAtSilence = TransportAction.Pause;
            _gain.DeclickOut();
        }

        public void Resume(long deviceOutputFrame)
        {
            _actionAtSilence = TransportAction.None;
            _isPlaying = true;
            _gain.DeclickIn();

            // The device drained throughout the pause. Every voice has to stop counting those
            // frames against itself, or its reported position is that much too far along and stops
            // tracking the device at all.
            _voices.ResetScheduledOutputEpochs(_transportId, deviceOutputFrame);
        }

        public void Stop()
        {
            _actionAtSilence = TransportAction.Stop;
            _gain.DeclickOut();
        }

        public void Seek(long targetFrame, long absoluteOutputFrame, int crossFadeFrames = GainRamp.SeekCrossFadeFrames)
        {
            if (!_hasTimeline)
                return;

            _timelineFrame = Math.Clamp(targetFrame, 0, Math.Max(0, _timelineDurationFrames));
            _voices.FadeOutScheduledVoices(crossFadeFrames);
            _eventScheduler.ReconstructEventsAtTimelineFrame(_transportId, _timelineFrame, absoluteOutputFrame, crossFadeFrames);
        }

        // Media handed straight to a voice: no event above it, no plan, and no timeline behind it.
        public void PlayMedia(EngineCommand engineCommand, long absoluteOutputFrame)
        {
            var activationResult = _voices.Activate(VoiceStart.ForMedia(
                engineCommand.PlayingId,
                engineCommand.PostCompletion,
                engineCommand.Media,
                _parameterResolver.ResolveDirectMedia(),
                engineCommand.TargetFrame,
                engineCommand.ShouldLoop));
            if (activationResult == VoiceActivationResult.Refused)
                engineCommand.PostCompletion.Complete(PostOutcome.Refused, absoluteOutputFrame);
        }

        // An event with no timeline behind it, warmed on the control thread like any other but
        // sounding the moment its command is applied rather than on a frame.
        public void PostEvent(EngineCommand engineCommand, long absoluteOutputFrame)
        {
            _eventScheduler.StartPostedEvent(
                engineCommand.PlayingId,
                engineCommand.PostCompletion,
                engineCommand.GameObject,
                engineCommand.ResolvedEvent,
                absoluteOutputFrame);
        }

        public void RetargetPlayingId(EngineCommand engineCommand)
            => _eventScheduler.RetargetPostedEvent(
                engineCommand.PlayingId,
                engineCommand.PostCompletion,
                engineCommand.GameObject,
                engineCommand.ResolvedEvent);

        // A schedule for a transport that has since been replaced is dropped rather than counted:
        // nothing was lost, because the timeline it was meant for is gone.
        public void ScheduleEvent(EngineCommand engineCommand, long absoluteOutputFrame)
        {
            if (engineCommand.TransportId != _transportId
                || !_hasTimeline
                || engineCommand.TargetFrame < 0
                || engineCommand.TargetFrame >= _timelineDurationFrames)
            {
                engineCommand.PostCompletion.Complete(PostOutcome.Refused, absoluteOutputFrame);
                return;
            }
            if (!_eventScheduler.AddScheduledEvent(engineCommand))
            {
                _telemetry.CountUnscheduledEvent();
                engineCommand.PostCompletion.Complete(PostOutcome.Refused, absoluteOutputFrame);
            }
        }

        // Everything the post has sounding, and anything it would still have sounded.
        public void StopPlayingId(PlayingId playingId)
        {
            _voices.StopPlayingId(playingId);
            _eventScheduler.CancelScheduledEvent(playingId);
        }

        // The cues due on the frame the transport is on, fired before the block is rendered so they
        // sound from its first frame.
        public void StartDueEvents(long absoluteOutputFrame)
        {
            if (_hasTimeline)
                _eventScheduler.ActivateEventsAtTimelineFrame(_transportId, _timelineFrame, absoluteOutputFrame);
        }

        // How far the renderer may run before something is due to happen: the next scheduled cue,
        // the end of the timeline, or the end of a ramp a pause or stop is waiting on. Cutting the
        // block here is what keeps an onset on its exact frame rather than quantising it to the
        // block size.
        public int LimitBlockFrames(int maximumFrameCount)
        {
            var blockFrameCount = maximumFrameCount;
            if (_hasTimeline)
                blockFrameCount = (int)Math.Min(blockFrameCount, FramesUntilNextTimelineEvent());
            if (_actionAtSilence != TransportAction.None && _gain.IsRamping)
                blockFrameCount = Math.Min(blockFrameCount, _gain.RemainingFrames);
            return blockFrameCount;
        }

        public void ApplyGainTo(Bus bus, int frameCount) => bus.ApplyGain(_gain, frameCount);

        public void PrepareEndFade(int blockFrameCount)
        {
            if (_isPlaying
                && !_isLooping
                && _timelineDurationFrames > 0
                && _timelineFrame + blockFrameCount >= _timelineDurationFrames)
                _voices.FadeOutScheduledVoices(Math.Max(1, blockFrameCount));
        }

        // Moves the scheduled timeline on by the block that was just rendered.
        public TransportAdvance Advance(int blockFrameCount)
        {
            _timelineFrame += blockFrameCount;
            if (_timelineDurationFrames <= 0 || _timelineFrame < _timelineDurationFrames)
                return TransportAdvance.Running(blockFrameCount);

            if (_isLooping)
            {
                _timelineFrame = 0;
                return TransportAdvance.Running(blockFrameCount);
            }

            _voices.FinaliseScheduledFades();
            _voices.DeactivateScheduledVoices();
            _isPlaying = false;
            _isCompleting = false;
            return TransportAdvance.Completed(blockFrameCount);
        }

        public bool TryFinishCompletionTail()
        {
            if (!_isCompleting || _voices.HasActiveScheduledVoice(_transportId))
                return false;
            _isCompleting = false;
            return true;
        }

        // Pause and stop are asked for on the control thread but only take effect here, once the
        // output has reached silence. Returns the action that landed, so the renderer can clear
        // what a stop leaves behind.
        public TransportAction ApplyPendingActionIfSilent()
        {
            if (_actionAtSilence == TransportAction.None || !_gain.IsSilent)
                return TransportAction.None;

            var transportAction = _actionAtSilence;
            _actionAtSilence = TransportAction.None;
            _isPlaying = false;
            if (transportAction == TransportAction.Pause)
                return TransportAction.Pause;

            _voices.DeactivateScheduledVoices();
            _eventScheduler.ClearScheduledEvents();
            _hasTimeline = false;
            _timelineFrame = 0;
            return TransportAction.Stop;
        }

        // Anything that starts the transport cancels a pause or stop that had not yet landed, and
        // opens at full gain.
        private void Begin()
        {
            _actionAtSilence = TransportAction.None;
            _isCompleting = false;
            _gain.SetImmediately(1f);
        }

        private long FramesUntilNextTimelineEvent()
        {
            var framesUntilNextEvent = _eventScheduler.FramesUntilNextStart(_timelineFrame);
            if (_timelineDurationFrames > 0)
                framesUntilNextEvent = Math.Min(framesUntilNextEvent, _timelineDurationFrames - _timelineFrame);
            return Math.Max(1, framesUntilNextEvent);
        }
    }
}
