using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Commands;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Editors.Audio.Shared.Wwise.Engine.Timing;

namespace Editors.Audio.Shared.Wwise.Engine.Scheduling
{
    internal sealed class EventScheduler
    {
        private readonly ScheduledEvent[] _scheduledEvents;

        // The scheduled events that are still waiting to start, ordered by the frame they
        // start on, with a cursor into them. The timeline advances a frame at a time, so
        // each frame only has to look at the entries that begin on it rather than at every
        // slot the scheduler could hold.
        private readonly ScheduledEvent[] _startOrder;

        // Where a cue's sounds land when it fires. Pre-allocated because firing happens inside the
        // render callback, where a GC pause costs a buffer.
        private readonly SelectedSound[] _selectedSounds;
        private readonly VoicePool _voices;
        private readonly ParameterResolver _parameterResolver;
        private readonly PendingTransitionQueue _pendingTransitions;
        private readonly EngineTelemetry _telemetry;
        private int _startOrderCount;
        private int _nextStartIndex;
        private bool _isStartOrderStale = true;
        private long _lastTimelineFrame = long.MinValue;

        public EventScheduler(
            VoicePool voices,
            EngineTelemetry telemetry,
            ParameterResolver parameterResolver,
            PendingTransitionQueue pendingTransitions,
            int maximumScheduledEvents,
            int maximumVoices)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumScheduledEvents);
            _scheduledEvents = Enumerable.Range(0, maximumScheduledEvents)
                .Select(_ => new ScheduledEvent())
                .ToArray();
            _startOrder = new ScheduledEvent[maximumScheduledEvents];
            _selectedSounds = new SelectedSound[maximumVoices];
            _voices = voices;
            _telemetry = telemetry;
            _parameterResolver = parameterResolver;
            _pendingTransitions = pendingTransitions;
        }

        public bool AddScheduledEvent(EngineCommand engineCommand)
        {
            foreach (var scheduledEvent in _scheduledEvents)
            {
                if (scheduledEvent.IsActive)
                    continue;
                scheduledEvent.ApplyScheduleCommand(engineCommand);
                _isStartOrderStale = true;
                return true;
            }
            return false;
        }

        public void ActivateEventsAtTimelineFrame(TransportId transportId, long timelineFrame, long absoluteOutputFrame)
        {
            if (_isStartOrderStale)
                RebuildStartOrder(timelineFrame);
            else if (timelineFrame < _lastTimelineFrame)
                SeekStartCursor(timelineFrame);
            else
                AdvanceStartCursor(timelineFrame);
            _lastTimelineFrame = timelineFrame;

            while (_nextStartIndex < _startOrderCount
                && _startOrder[_nextStartIndex].TimelineStartFrame == timelineFrame)
            {
                var scheduledEvent = _startOrder[_nextStartIndex];
                _nextStartIndex++;
                if (!scheduledEvent.ShouldStartAtTimelineFrame(transportId, timelineFrame))
                    continue;

                RunActions(
                    scheduledEvent.Plan,
                    scheduledEvent.PlayingId,
                    scheduledEvent.PostCompletion,
                    scheduledEvent.GameObject,
                    isScheduledVoice: true,
                    elapsedFrames: 0,
                    absoluteOutputFrame,
                    forcedFadeInFrames: 0);
            }
        }

        // An event posted with no timeline behind it starts here too, so that a cue coming due and a
        // post arriving take the same path through the walker and into the voice pool.
        public void StartPostedEvent(
            PlayingId playingId,
            PostCompletionState postCompletion,
            GameObjectId gameObject,
            ResolvedEvent plan,
            long absoluteOutputFrame)
            => RunActions(plan, playingId, postCompletion, gameObject, isScheduledVoice: false, elapsedFrames: 0, absoluteOutputFrame, forcedFadeInFrames: 0);

        public void RetargetPostedEvent(
            PlayingId playingId,
            PostCompletionState postCompletion,
            GameObjectId gameObject,
            ResolvedEvent plan)
        {
            var selectedSoundCount = NodeWalker.Select(plan, _selectedSounds, out var droppedSoundCount);
            _telemetry.CountUnstartedVoices(droppedSoundCount);
            _voices.Retarget(
                playingId,
                postCompletion,
                gameObject,
                _selectedSounds,
                selectedSoundCount,
                _parameterResolver);
        }

        // The upper engine running inside the render callback: the event's actions are carried out
        // in the order they were authored, a container picks, and the sounds it landed on become
        // voices. No hierarchy lookup, no media cache and no allocation happen in here — everything
        // it reads was attached at warm time.
        //
        // `elapsedFrames` is how far past its start the cue already is, which is not zero when a seek
        // lands inside a cue and it has to be reconstructed part way through.
        private void RunActions(
            ResolvedEvent plan,
            PlayingId playingId,
            PostCompletionState postCompletion,
            GameObjectId gameObject,
            bool isScheduledVoice,
            long elapsedFrames,
            long absoluteOutputFrame,
            int forcedFadeInFrames)
        {
            var selectedSoundCount = 0;
            var startedVoiceCount = 0;
            var droppedSoundCount = 0;
            var finishedBeforeSeekCount = 0;
            var carriedOutOtherAction = false;

            foreach (var action in plan.Actions)
            {
                switch (action.Kind)
                {
                    case ResolvedActionKind.Play:
                        StartAction(
                            plan,
                            action,
                            playingId,
                            postCompletion,
                            gameObject,
                            isScheduledVoice,
                            elapsedFrames,
                            ref selectedSoundCount,
                            ref startedVoiceCount,
                            ref droppedSoundCount,
                            ref finishedBeforeSeekCount,
                            forcedFadeInFrames);
                        break;

                    case ResolvedActionKind.Stop:
                    case ResolvedActionKind.Pause:
                    case ResolvedActionKind.Resume:
                        _voices.ApplyAction(
                            action.Kind,
                            action.TargetNodeId,
                            gameObject,
                            action.IsScopedToGameObject,
                            FramesFrom(action.FadeMilliseconds),
                            absoluteOutputFrame);
                        carriedOutOtherAction = true;
                        break;

                    case ResolvedActionKind.Transition:
                        if (action.TransitionIndex >= 0 && action.TransitionIndex < plan.Transitions.Length)
                            _pendingTransitions.TryPublish(plan.Transitions[action.TransitionIndex]);
                        carriedOutOtherAction = true;
                        break;
                }
            }

            _telemetry.CountUnstartedVoices(droppedSoundCount);
            if (startedVoiceCount > 0)
                return;

            // Nothing is sounding for this post, so it has to finish here rather than be waited on.
            // Which outcome it finished with is the difference between a cue that had nothing to
            // play, one whose voices were all refused, one the transport has already run past, and
            // one that only ever meant to stop something.
            if (selectedSoundCount > 0 || droppedSoundCount > 0)
                postCompletion.Complete(PostOutcome.Refused, absoluteOutputFrame);
            else if (finishedBeforeSeekCount > 0 || carriedOutOtherAction)
                postCompletion.Complete(PostOutcome.Played, absoluteOutputFrame);
            else
            {
                _telemetry.CountSilentCue();
                postCompletion.Complete(PostOutcome.Silent, absoluteOutputFrame);
            }
        }

        private void StartAction(
            ResolvedEvent plan,
            in ResolvedAction action,
            PlayingId playingId,
            PostCompletionState postCompletion,
            GameObjectId gameObject,
            bool isScheduledVoice,
            long elapsedFrames,
            ref int selectedSoundCount,
            ref int startedVoiceCount,
            ref int droppedSoundCount,
            ref int finishedBeforeSeekCount,
            int forcedFadeInFrames)
        {
            var selectedCount = NodeWalker.SelectFromRoot(plan, action.RootNodeIndex, _selectedSounds, out var droppedCount);
            selectedSoundCount += selectedCount;
            droppedSoundCount += droppedCount;

            // Randomised per instance, like every other authored property: two footsteps from the
            // same action do not both land exactly 200 ms late.
            var delayFrames = FramesFrom(_parameterResolver.Resolve(action.DelayMilliseconds));
            var fadeFrames = FramesFrom(action.FadeMilliseconds);

            // A cue the transport already ran past starts part way through, and the delay is part of
            // what it ran past: a sound 200 ms behind its cue is only 100 ms old when the cue is 300.
            var elapsedPlayFrames = elapsedFrames - delayFrames;
            var remainingDelayFrames = elapsedFrames > 0 ? Math.Max(0, delayFrames - elapsedFrames) : delayFrames;
            var initialMixFrame = Math.Max(0, elapsedPlayFrames);

            for (var selectedOrdinal = 0; selectedOrdinal < selectedCount; selectedOrdinal++)
            {
                var selectedSound = _selectedSounds[selectedOrdinal];

                // A sound the seek is already past the end of does not start at all.
                if (elapsedPlayFrames > 0 && elapsedPlayFrames >= PlaybackTime.ToFrames(selectedSound.Media.Duration))
                {
                    selectedSoundCount--;
                    finishedBeforeSeekCount++;
                    continue;
                }

                var voiceStart = new VoiceStart(
                    playingId,
                    postCompletion,
                    selectedSound.Media,
                    _parameterResolver.Resolve(selectedSound.Parameters),
                    selectedSound.Limit,
                    selectedSound.Limits,
                    selectedSound.NodeId,
                    gameObject,
                    selectedSound.AncestorNodeIds,
                    initialMixFrame,
                    (int)remainingDelayFrames + selectedSound.ContainerDelayFrames,
                    Math.Max(fadeFrames, selectedSound.ContainerFadeInFrames),
                    IsLooping: false,
                    isScheduledVoice,
                    selectedSound.ContainerFadeOutFrames,
                    selectedSound.ContainerEndFadeOutFrames,
                    selectedSound.UsesEqualPowerCrossfade,
                    selectedSound.LoopCount,
                    selectedSound.Attenuation,
                    selectedSound.GameObjectParameters,
                    selectedSound.IsPositioned,
                    selectedSound.VirtualQueueBehaviour,
                    selectedSound.BelowThresholdBehaviour);
                // A reconstructed voice fades in over the span the voices it replaces are fading
                // out over. Two halves of one cross-fade that disagree on length leave a step.
                if (forcedFadeInFrames > 0)
                    voiceStart = voiceStart with
                    {
                        ForceFadeIn = true,
                        FadeInFrames = Math.Max(voiceStart.FadeInFrames, forcedFadeInFrames)
                    };

                if (_voices.Activate(voiceStart) != VoiceActivationResult.Refused)
                    startedVoiceCount++;
            }

            if (_voices.RegisterContinuations(
                    plan,
                    playingId,
                    postCompletion,
                    gameObject,
                    isScheduledVoice,
                    _selectedSounds,
                    selectedCount,
                    _parameterResolver) > 0)
                startedVoiceCount++;
        }

        private static int FramesFrom(float milliseconds)
            => (int)Math.Max(0, MathF.Round(milliseconds * PlaybackFormat.SampleRate / 1000f));

        private static int FramesFrom(in RandomisedProperty milliseconds) => FramesFrom(milliseconds.Value);

        // Rebuilt in place rather than reallocated, because this runs inside the output
        // callback whenever the set of scheduled events changes.
        private void RebuildStartOrder(long timelineFrame)
        {
            _startOrderCount = 0;
            foreach (var scheduledEvent in _scheduledEvents)
            {
                if (scheduledEvent.IsActive)
                    _startOrder[_startOrderCount++] = scheduledEvent;
            }

            // Insertion sort: the list is small, usually already ordered, and this keeps the
            // rebuild allocation free.
            for (var unsortedIndex = 1; unsortedIndex < _startOrderCount; unsortedIndex++)
            {
                var scheduledEvent = _startOrder[unsortedIndex];
                var sortedIndex = unsortedIndex - 1;
                while (sortedIndex >= 0 && _startOrder[sortedIndex].TimelineStartFrame > scheduledEvent.TimelineStartFrame)
                {
                    _startOrder[sortedIndex + 1] = _startOrder[sortedIndex];
                    sortedIndex--;
                }
                _startOrder[sortedIndex + 1] = scheduledEvent;
            }

            Array.Clear(_startOrder, _startOrderCount, _startOrder.Length - _startOrderCount);
            _isStartOrderStale = false;
            SeekStartCursor(timelineFrame);
        }

        // Events whose start frame has already gone by are passed over, which is what
        // looping and seeking need: a pass only starts the cues still ahead of it.
        private void SeekStartCursor(long timelineFrame)
        {
            _nextStartIndex = 0;
            AdvanceStartCursor(timelineFrame);
        }

        // Forward only, so the ordinary case costs nothing. The renderer cuts a block short
        // wherever a cue is due, so it arrives here on the exact frame and the cursor is already
        // where it needs to be.
        private void AdvanceStartCursor(long timelineFrame)
        {
            while (_nextStartIndex < _startOrderCount
                && _startOrder[_nextStartIndex].TimelineStartFrame < timelineFrame)
                _nextStartIndex++;
        }

        // How far the renderer may run before the next cue is due. Read after the cues on the
        // current frame have started, so it is always at least one frame.
        public long FramesUntilNextStart(long timelineFrame)
            => _nextStartIndex < _startOrderCount
                ? _startOrder[_nextStartIndex].TimelineStartFrame - timelineFrame
                : long.MaxValue;

        // A cue the seek landed inside is picked again rather than resumed, because what it played
        // last time was never written down — the pick is the point. Scrubbing back over a cue can
        // therefore land on a different variation, which is what the game would do too.
        public void ReconstructEventsAtTimelineFrame(TransportId transportId, long timelineFrame, long absoluteOutputFrame, int fadeInFrames)
        {
            if (_isStartOrderStale)
                RebuildStartOrder(timelineFrame);
            else
                SeekStartCursor(timelineFrame);
            _lastTimelineFrame = timelineFrame;

            foreach (var scheduledEvent in _scheduledEvents)
            {
                if (!scheduledEvent.CanReconstruct(transportId))
                    continue;

                // Measured in mix frames rather than the media's own, because the timeline is the
                // mix clock and the sources may have been authored at any rate.
                var elapsedTimelineFrames = timelineFrame - scheduledEvent.TimelineStartFrame;
                if (elapsedTimelineFrames <= 0)
                    continue;

                RunActions(
                    scheduledEvent.Plan,
                    scheduledEvent.PlayingId,
                    scheduledEvent.PostCompletion,
                    scheduledEvent.GameObject,
                    isScheduledVoice: true,
                    elapsedTimelineFrames,
                    absoluteOutputFrame,
                    forcedFadeInFrames: fadeInFrames);
            }
        }

        public void CancelScheduledEvent(PlayingId playingId)
        {
            foreach (var scheduledEvent in _scheduledEvents)
            {
                if (!scheduledEvent.Matches(playingId))
                    continue;
                scheduledEvent.Deactivate();
                _isStartOrderStale = true;
            }
        }

        public void ClearScheduledEvents()
        {
            foreach (var scheduledEvent in _scheduledEvents)
            {
                scheduledEvent.Deactivate();
            }
            _isStartOrderStale = true;
        }
    }
}
