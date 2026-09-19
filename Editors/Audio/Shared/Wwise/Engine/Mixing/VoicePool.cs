using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Rendering;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Editors.Audio.Shared.Wwise.Engine.Dsp;

namespace Editors.Audio.Shared.Wwise.Engine.Mixing
{
    internal enum VoiceActivationResult
    {
        Refused,
        Started,
        Virtualised,
        Replaced
    }

    // The voices, and who gets to be one.
    //
    // Two budgets decide that: how many voices may sound at once at all, and how many instances the
    // node a sound came from allows. Where either is reached, the lowest-priority voice competing
    // for it gives way. The physical pool requires a newcomer to outrank it; an authored instance
    // limit chooses discard-oldest or discard-newest when priorities are equal. A sound that cannot
    // start is counted rather than lost quietly.
    internal sealed class VoicePool
    {
        private sealed class ContinuousPlayback
        {
            public bool IsActive;
            public ResolvedEvent Plan;
            public int RootNodeIndex;
            public PlayingId PlayingId;
            public PostCompletionState PostCompletion;
            public GameObjectId GameObject;
            public bool IsScheduledVoice;
            public int RemainingFrames;
            public ParameterResolver ParameterResolver;
        }

        private readonly Voice[] _voices;
        private readonly Voice[] _replacementCandidates;
        private readonly VoiceStart[] _seekStarts;
        private readonly ContinuousPlayback[] _continuousPlaybacks;
        private readonly SelectedSound[] _continuationSounds;
        private readonly EngineTelemetry _telemetry;

        // How many voices may be sounding. The array is larger, because a voice that has given up
        // its place ramps out rather than being cut and holds its slot until the ramp finishes —
        // so the headroom is what lets stealing declick instead of clicking.
        private readonly int _maximumSoundingVoices;

        // What makes one voice older than another, so that equally unimportant voices give way in
        // the order they started.
        private long _nextStartOrdinal;
        private long _nextRetargetGeneration;

        public VoicePool(int maximumSoundingVoices, EngineTelemetry telemetry)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSoundingVoices);
            _maximumSoundingVoices = maximumSoundingVoices;
            _telemetry = telemetry;
            _voices = Enumerable.Range(0, maximumSoundingVoices * 2).Select(_ => new Voice(telemetry)).ToArray();
            _replacementCandidates = new Voice[_voices.Length];
            _seekStarts = new VoiceStart[_voices.Length];
            _continuousPlaybacks = Enumerable.Range(0, _voices.Length).Select(_ => new ContinuousPlayback()).ToArray();
            _continuationSounds = new SelectedSound[_voices.Length];
        }

        public VoiceActivationResult Activate(in VoiceStart start)
        {
            var room = TryMakeRoomFor(start, out var replacedVoice);
            if (room == VoiceRoom.Refused)
            {
                _telemetry.CountUnstartedVoice();
                return VoiceActivationResult.Refused;
            }

            var voice = FindIdleVoice();
            if (voice == null)
            {
                // Only reachable when more voices are ramping out than the pool is allowed to have
                // sounding, which takes a storm of steals inside one de-click ramp.
                _telemetry.CountUnstartedVoice();
                return VoiceActivationResult.Refused;
            }

            if (room == VoiceRoom.Virtualised)
            {
                voice.ActivateVirtual(start, ++_nextStartOrdinal);
                _telemetry.CountOverLimitVirtualisedVoice();
                return VoiceActivationResult.Virtualised;
            }

            voice.Activate(start, ++_nextStartOrdinal);
            return replacedVoice ? VoiceActivationResult.Replaced : VoiceActivationResult.Started;
        }

        // Returns the number of leading frames of the block that had at least one voice in them.
        // Voices only ever drop out within a block, so the count is a prefix rather than a total.
        public int MixBlock(
            Bus bus,
            long firstAbsoluteOutputFrame,
            int frameCount)
            => MixBlock(
                bus,
                bus,
                busGraph: null,
                default,
                shouldRouteScheduledVoices: false,
                shouldMixTimelineVoices: true,
                firstAbsoluteOutputFrame,
                frameCount);

        public int MixBlock(
            BusGraph busGraph,
            TransportId timelineId,
            bool shouldMixTimelineVoices,
            long firstAbsoluteOutputFrame,
            int frameCount)
            => MixBlock(
                timelineBus: null,
                immediateBus: null,
                busGraph,
                timelineId,
                shouldRouteScheduledVoices: true,
                shouldMixTimelineVoices,
                firstAbsoluteOutputFrame,
                frameCount);

        private int MixBlock(
            Bus timelineBus,
            Bus immediateBus,
            BusGraph busGraph,
            TransportId timelineId,
            bool shouldRouteScheduledVoices,
            bool shouldMixTimelineVoices,
            long firstAbsoluteOutputFrame,
            int frameCount)
        {
            PromoteVirtualVoices();
            var audibleFrameCount = 0;
            var soundingVoiceCount = 0;
            var physicalVoiceCount = 0;
            var virtualVoiceCount = 0;

            foreach (var voice in _voices)
            {
                var isTimelineVoice = shouldRouteScheduledVoices
                    && voice.IsScheduledVoice
                    && voice.PlayingId.TransportId == timelineId;
                if (isTimelineVoice && !shouldMixTimelineVoices)
                    continue;

                var playingId = voice.PlayingId;
                var postCompletion = voice.PostCompletion;
                var bus = busGraph != null
                    ? busGraph.Route(voice.OutputBusId, isTimelineVoice)
                    : isTimelineVoice ? timelineBus : immediateBus;
                var voiceFrameCount = voice.MixBlock(
                    bus.Samples,
                    firstAbsoluteOutputFrame,
                    frameCount,
                    out var hasEnded);

                // A post is finished when its last voice is, not when its first is: a layer
                // container sounds several at once and the shorter layers run out first.
                if (hasEnded && !HasActiveVoiceFor(playingId))
                    postCompletion?.Complete(PostOutcome.Played, firstAbsoluteOutputFrame + voiceFrameCount);
                if (voiceFrameCount > audibleFrameCount)
                    audibleFrameCount = voiceFrameCount;
                if (voice.IsSounding)
                    soundingVoiceCount++;
                if (voice.OccupiesPhysicalVoiceBudget)
                    physicalVoiceCount++;
                if (voice.IsVirtual)
                    virtualVoiceCount++;
            }

            _telemetry.RecordSoundingVoiceCount(soundingVoiceCount);
            _telemetry.RecordVoiceCounts(physicalVoiceCount, virtualVoiceCount);
            return audibleFrameCount;
        }

        private void PromoteVirtualVoices()
        {
            var physicalVoiceCount = 0;
            foreach (var voice in _voices)
            {
                if (voice.OccupiesPhysicalVoiceBudget)
                    physicalVoiceCount++;
            }
            if (physicalVoiceCount >= _maximumSoundingVoices)
                return;

            foreach (var candidate in _voices)
            {
                if (!candidate.IsOverLimitVirtual || !HasRoomForVirtual(candidate))
                    continue;
                candidate.ReturnToPhysical();
                physicalVoiceCount++;
                if (physicalVoiceCount >= _maximumSoundingVoices)
                    return;
            }
        }

        private bool HasRoomForVirtual(Voice candidate)
        {
            foreach (var limit in candidate.Limits.Span)
            {
                if (!limit.IsLimited)
                    continue;
                var physicalCount = 0;
                foreach (var voice in _voices)
                {
                    if (ReferenceEquals(voice, candidate)
                        || !voice.OccupiesInstanceBudget
                        || !voice.IsCountedAgainst(limit, candidate.GameObject))
                        continue;
                    physicalCount++;
                }
                if (physicalCount >= limit.MaximumInstanceCount)
                    return false;
            }
            return true;
        }

        public bool TryGetPosition(PlayingId playingId, long? absoluteOutputFrame, out long mixFrame)
        {
            foreach (var voice in _voices)
            {
                if (!voice.IsStopping && voice.TryGetPosition(playingId, absoluteOutputFrame, out mixFrame))
                    return true;
            }

            foreach (var voice in _voices)
            {
                if (voice.TryGetPosition(playingId, absoluteOutputFrame, out mixFrame))
                    return true;
            }

            mixFrame = 0;
            return false;
        }

        // Every voice of the post, because a playing id names a post rather than a voice.
        public void StopPlayingId(PlayingId playingId)
        {
            foreach (var voice in _voices)
            {
                voice.Cancel(playingId);
            }
            foreach (var playback in _continuousPlaybacks)
            {
                if (playback.IsActive && playback.PlayingId == playingId)
                    playback.IsActive = false;
            }
        }

        public void PausePlayingId(PlayingId playingId)
        {
            foreach (var voice in _voices)
                voice.Pause(playingId);
        }

        public void ResumePlayingId(PlayingId playingId, long deviceOutputFrame)
        {
            foreach (var voice in _voices)
                voice.Resume(playingId, deviceOutputFrame);
        }

        public void SeekPlayingId(PlayingId playingId, long mixFrame)
        {
            var seekStartCount = 0;
            foreach (var voice in _voices)
            {
                if (voice.TryCreateSeekStart(playingId, mixFrame, out var start))
                    _seekStarts[seekStartCount++] = start;
            }
            for (var seekStartIndex = 0; seekStartIndex < seekStartCount; seekStartIndex++)
                Activate(_seekStarts[seekStartIndex]);
        }

        public int RegisterContinuations(
            ResolvedEvent plan,
            PlayingId playingId,
            PostCompletionState postCompletion,
            GameObjectId gameObject,
            bool isScheduledVoice,
            SelectedSound[] selectedSounds,
            int selectedSoundCount,
            ParameterResolver parameterResolver)
        {
            var registeredCount = 0;
            for (var selectedIndex = 0; selectedIndex < selectedSoundCount; selectedIndex++)
            {
                var selected = selectedSounds[selectedIndex];
                if (selected.ContinuationNodeIndex < 0)
                    continue;
                var playback = FindContinuousPlayback(playingId, selected.ContinuationNodeIndex) ?? FindIdleContinuousPlayback();
                if (playback == null)
                {
                    _telemetry.CountUnstartedVoice();
                    continue;
                }

                playback.IsActive = true;
                playback.Plan = plan;
                playback.RootNodeIndex = selected.ContinuationNodeIndex;
                playback.PlayingId = playingId;
                playback.PostCompletion = postCompletion;
                playback.GameObject = gameObject;
                playback.IsScheduledVoice = isScheduledVoice;
                playback.RemainingFrames = Math.Max(playback.RemainingFrames, selected.ContinuationFrames);
                playback.ParameterResolver = parameterResolver;
                registeredCount++;

                // Every sound selected by this one continuous root carries the same marker.
                while (selectedIndex + 1 < selectedSoundCount
                    && selectedSounds[selectedIndex + 1].ContinuationNodeIndex == selected.ContinuationNodeIndex)
                    selectedIndex++;
            }
            return registeredCount;
        }

        public void StartDueContinuations(TransportId timelineId, bool shouldMixTimelineVoices, long absoluteOutputFrame)
        {
            foreach (var playback in _continuousPlaybacks)
            {
                if (!playback.IsActive || playback.RemainingFrames > 0)
                    continue;
                if (playback.IsScheduledVoice
                    && (playback.PlayingId.TransportId != timelineId || !shouldMixTimelineVoices))
                    continue;

                var selectedCount = NodeWalker.SelectFromRoot(
                    playback.Plan,
                    playback.RootNodeIndex,
                    _continuationSounds,
                    out var droppedSoundCount);
                _telemetry.CountUnstartedVoices(droppedSoundCount);
                playback.RemainingFrames = 0;
                for (var selectedIndex = 0; selectedIndex < selectedCount; selectedIndex++)
                {
                    var selected = _continuationSounds[selectedIndex];
                    Activate(CreateContinuationStart(playback, selected));
                    playback.RemainingFrames = Math.Max(playback.RemainingFrames, selected.ContinuationFrames);
                }

                if (selectedCount != 0 && playback.RemainingFrames > 0)
                    continue;
                playback.IsActive = false;
                if (!HasActiveVoiceFor(playback.PlayingId))
                    playback.PostCompletion?.Complete(PostOutcome.Played, absoluteOutputFrame);
            }
        }

        public int FramesUntilContinuation(TransportId timelineId, bool shouldMixTimelineVoices)
        {
            var frames = int.MaxValue;
            foreach (var playback in _continuousPlaybacks)
            {
                if (!playback.IsActive)
                    continue;
                if (playback.IsScheduledVoice
                    && (playback.PlayingId.TransportId != timelineId || !shouldMixTimelineVoices))
                    continue;
                frames = Math.Min(frames, Math.Max(1, playback.RemainingFrames));
            }
            return frames;
        }

        public void AdvanceContinuations(TransportId timelineId, bool shouldMixTimelineVoices, int frameCount)
        {
            foreach (var playback in _continuousPlaybacks)
            {
                if (!playback.IsActive)
                    continue;
                if (playback.IsScheduledVoice
                    && (playback.PlayingId.TransportId != timelineId || !shouldMixTimelineVoices))
                    continue;
                playback.RemainingFrames = Math.Max(0, playback.RemainingFrames - frameCount);
            }
        }

        private VoiceStart CreateContinuationStart(ContinuousPlayback playback, in SelectedSound selected)
            => new(
                playback.PlayingId,
                playback.PostCompletion,
                selected.Media,
                playback.ParameterResolver.Resolve(selected.Parameters),
                selected.Limit,
                selected.Limits,
                selected.NodeId,
                playback.GameObject,
                selected.AncestorNodeIds,
                InitialMixFrame: 0,
                selected.ContainerDelayFrames,
                selected.ContainerFadeInFrames,
                IsLooping: false,
                playback.IsScheduledVoice,
                selected.ContainerFadeOutFrames,
                selected.ContainerEndFadeOutFrames,
                selected.UsesEqualPowerCrossfade,
                selected.LoopCount,
                selected.Attenuation,
                selected.GameObjectParameters,
                selected.IsPositioned,
                selected.VirtualQueueBehaviour,
                selected.BelowThresholdBehaviour);

        private ContinuousPlayback FindContinuousPlayback(PlayingId playingId, int rootNodeIndex)
        {
            foreach (var playback in _continuousPlaybacks)
            {
                if (playback.IsActive && playback.PlayingId == playingId && playback.RootNodeIndex == rootNodeIndex)
                    return playback;
            }
            return null;
        }

        private ContinuousPlayback FindIdleContinuousPlayback()
        {
            foreach (var playback in _continuousPlaybacks)
            {
                if (!playback.IsActive)
                    return playback;
            }
            return null;
        }

        // What an event's Stop, Pause or Resume action reaches. Returns how many voices it moved,
        // so a post that stops nothing can be told apart from one that stopped something.
        public int ApplyAction(
            ResolvedActionKind actionKind,
            uint targetNodeId,
            GameObjectId gameObject,
            bool isScopedToGameObject,
            int fadeFrames,
            long deviceOutputFrame)
        {
            var affectedVoiceCount = 0;
            foreach (var voice in _voices)
            {
                if (voice.ApplyAction(actionKind, targetNodeId, gameObject, isScopedToGameObject, fadeFrames, deviceOutputFrame))
                    affectedVoiceCount++;
            }
            return affectedVoiceCount;
        }

        public void ResetScheduledOutputEpochs(TransportId transportId, long deviceOutputFrame)
        {
            foreach (var voice in _voices)
            {
                if (voice.IsScheduledVoice && voice.PlayingId.TransportId == transportId)
                    voice.ResetOutputEpoch(deviceOutputFrame);
            }
        }

        public void DeactivateScheduledVoices()
        {
            foreach (var voice in _voices)
            {
                voice.DeactivateIfScheduled();
            }
            DeactivateScheduledContinuations();
        }

        public void FadeOutScheduledVoices(int rampFrames = GainRamp.DeclickFrames)
        {
            foreach (var voice in _voices)
                voice.FadeOutIfScheduled(rampFrames);
            DeactivateScheduledContinuations();
        }

        public void FinaliseScheduledFades()
        {
            foreach (var voice in _voices)
                voice.DeactivateIfFinishedScheduledFade();
        }

        public bool HasActiveScheduledVoice(TransportId transportId)
        {
            foreach (var voice in _voices)
            {
                if (voice.IsActive && voice.IsScheduledVoice && voice.PlayingId.TransportId == transportId)
                    return true;
            }
            return false;
        }

        public void DeactivateAll()
        {
            foreach (var voice in _voices)
            {
                voice.Deactivate();
            }
            foreach (var playback in _continuousPlaybacks)
                playback.IsActive = false;
        }

        // Both budgets have to hold, and each is the same question asked of a different set of
        // voices: the node's own instances, then the pool as a whole.
        private VoiceRoom TryMakeRoomFor(in VoiceStart start, out bool replacedVoice)
        {
            replacedVoice = false;
            var replacementCount = 0;
            foreach (var limit in start.Limits.Span)
            {
                if (limit.IsLimited && !TryFindRoomAmong(
                        start.Parameters.Priority,
                        limit,
                        start.GameObject,
                        limit.MaximumInstanceCount,
                        ref replacementCount))
                    return limit.OverLimitBehaviour == VoiceOverLimitBehaviour.Virtualise
                        ? VoiceRoom.Virtualised
                        : VoiceRoom.Refused;
            }
            if (!TryFindPhysicalRoom(start.Parameters.Priority, _maximumSoundingVoices, ref replacementCount))
                return VoiceRoom.Refused;

            for (var replacementIndex = 0; replacementIndex < replacementCount; replacementIndex++)
            {
                _replacementCandidates[replacementIndex].Steal();
                _telemetry.CountStolenVoice();
            }
            replacedVoice = replacementCount != 0;
            return VoiceRoom.Available;
        }

        // A limit node of zero counts every sounding voice, which is how the pool budget and a node
        // instance limit come to be one rule rather than two.
        private bool TryFindRoomAmong(
            float priority,
            in VoiceLimit limit,
            GameObjectId gameObject,
            int maximumSoundingCount,
            ref int replacementCount)
        {
            var soundingCount = 0;
            Voice lowestPriorityVoice = null;

            foreach (var voice in _voices)
            {
                if (!voice.OccupiesInstanceBudget || !voice.IsCountedAgainst(limit, gameObject))
                    continue;

                if (IsReplacementCandidate(voice, replacementCount))
                    continue;

                soundingCount++;
                if (lowestPriorityVoice == null || voice.GivesWayBefore(lowestPriorityVoice))
                    lowestPriorityVoice = voice;
            }

            if (soundingCount < maximumSoundingCount)
                return true;

            // Higher priority always wins. At equal priority the authored reached behaviour decides
            // whether the oldest incumbent or the newcomer is discarded.
            if (lowestPriorityVoice == null || lowestPriorityVoice.Priority > priority)
                return false;
            if (lowestPriorityVoice.Priority == priority
                && limit.ReachedBehaviour == VoiceLimitReachedBehaviour.DiscardNewest)
                return false;

            _replacementCandidates[replacementCount++] = lowestPriorityVoice;
            return true;
        }

        private bool TryFindPhysicalRoom(float priority, int maximumSoundingCount, ref int replacementCount)
        {
            var soundingCount = 0;
            Voice lowestPriorityVoice = null;
            foreach (var voice in _voices)
            {
                if (!voice.OccupiesPhysicalVoiceBudget || IsReplacementCandidate(voice, replacementCount))
                    continue;

                soundingCount++;
                if (lowestPriorityVoice == null || voice.GivesWayBefore(lowestPriorityVoice))
                    lowestPriorityVoice = voice;
            }

            if (soundingCount < maximumSoundingCount)
                return true;
            if (lowestPriorityVoice == null || lowestPriorityVoice.Priority >= priority)
                return false;

            _replacementCandidates[replacementCount++] = lowestPriorityVoice;
            return true;
        }

        private enum VoiceRoom
        {
            Refused,
            Available,
            Virtualised
        }

        private bool IsReplacementCandidate(Voice voice, int replacementCount)
        {
            for (var replacementIndex = 0; replacementIndex < replacementCount; replacementIndex++)
            {
                if (ReferenceEquals(_replacementCandidates[replacementIndex], voice))
                    return true;
            }
            return false;
        }

        public void Retarget(
            PlayingId playingId,
            PostCompletionState postCompletion,
            GameObjectId gameObject,
            SelectedSound[] selectedSounds,
            int selectedSoundCount,
            ParameterResolver parameterResolver)
        {
            var generation = ++_nextRetargetGeneration;
            for (var selectedIndex = 0; selectedIndex < selectedSoundCount; selectedIndex++)
            {
                var selected = selectedSounds[selectedIndex];
                var parameters = parameterResolver.Resolve(selected.Parameters);
                foreach (var voice in _voices)
                {
                    if (voice.TryRetarget(
                            playingId,
                            selected.NodeId,
                            selected.Media,
                            parameters,
                            selected.ContainerFadeInFrames,
                            selected.ContainerFadeOutFrames,
                            generation,
                            selectedIndex))
                        break;
                }
            }

            foreach (var voice in _voices)
                voice.StopUnlessRetargeted(playingId, generation);

            TryGetPosition(playingId, absoluteOutputFrame: null, out var currentMixFrame);
            for (var selectedIndex = 0; selectedIndex < selectedSoundCount; selectedIndex++)
            {
                var selected = selectedSounds[selectedIndex];
                var wasRetargeted = false;
                foreach (var voice in _voices)
                {
                    if (!voice.WasRetargeted(playingId, selected.NodeId, selected.Media, generation, selectedIndex))
                        continue;
                    wasRetargeted = true;
                    break;
                }
                if (wasRetargeted)
                    continue;

                var parameters = parameterResolver.Resolve(selected.Parameters);
                var start = new VoiceStart(
                    playingId,
                    postCompletion,
                    selected.Media,
                    parameters,
                    selected.Limit,
                    selected.Limits,
                    selected.NodeId,
                    gameObject,
                    selected.AncestorNodeIds,
                    currentMixFrame,
                    selected.ContainerDelayFrames,
                    selected.ContainerFadeInFrames,
                    IsLooping: false,
                    IsScheduledVoice: false,
                    selected.ContainerFadeOutFrames,
                    LoopCount: selected.LoopCount,
                    Attenuation: selected.Attenuation,
                    GameObjectParameters: selected.GameObjectParameters,
                    IsPositioned: selected.IsPositioned,
                    VirtualQueueBehaviour: selected.VirtualQueueBehaviour,
                    BelowThresholdBehaviour: selected.BelowThresholdBehaviour);
                if (Activate(start) == VoiceActivationResult.Refused)
                    continue;
                foreach (var voice in _voices)
                {
                    if (voice.TryRetarget(
                            playingId,
                            selected.NodeId,
                            selected.Media,
                            parameters,
                            selected.ContainerFadeInFrames,
                            selected.ContainerFadeOutFrames,
                            generation,
                            selectedIndex))
                        break;
                }
            }
        }

        private Voice FindIdleVoice()
        {
            foreach (var voice in _voices)
            {
                if (!voice.IsActive)
                    return voice;
            }
            return null;
        }

        private bool HasActiveVoiceFor(PlayingId playingId)
        {
            foreach (var voice in _voices)
            {
                if (voice.Matches(playingId))
                    return true;
            }
            foreach (var playback in _continuousPlaybacks)
            {
                if (playback.IsActive && playback.PlayingId == playingId)
                    return true;
            }
            return false;
        }

        private void DeactivateScheduledContinuations()
        {
            foreach (var playback in _continuousPlaybacks)
            {
                if (playback.IsActive && playback.IsScheduledVoice)
                    playback.IsActive = false;
            }
        }
    }
}
