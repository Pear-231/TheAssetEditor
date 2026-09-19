using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Timing;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using System.Numerics;

namespace Editors.Audio.Shared.Wwise.Engine.Mixing
{
    internal sealed class Voice
    {
        private readonly EngineTelemetry _telemetry;
        private readonly GainRamp _gainRamp = new();
        private readonly GainRamp _volumeRamp = new();

        // The authored properties that move when a sounding voice is retargeted. Gain gets a
        // per-frame ramp above; these move per block, which is what their stages cost.
        private readonly ParameterRamp _pitchRamp = new();
        private readonly ParameterRamp _lowPassRamp = new();
        private readonly ParameterRamp _highPassRamp = new();

        // A fixed order of concrete stages rather than a plugin chain: voice DSP order is fixed in
        // every engine, so a list of interfaces would model something that never varies.
        private readonly Resampler _resampler = new();
        private readonly VoiceFilter _filter = new();
        private readonly Panner _panner = new();

        private PlayingId _playingId;
        private PostCompletionState _postCompletion;
        private VoiceParameters _parameters = VoiceParameters.Neutral;
        private ReadOnlyMemory<VoiceLimit> _limits;
        private SourceMedia _media;
        private uint _nodeId;
        private GameObjectId _gameObject;
        private ReadOnlyMemory<uint> _ancestorNodeIds;
        private long _startOrdinal;
        private long _currentMixFrame;
        private long _firstMixFrame;
        private long _firstAbsoluteOutputFrame;
        private int _remainingStartDelayFrames;
        private bool _isLooping;
        private int _remainingPlayCount;
        private bool _isScheduledVoice;
        private bool _wasStolen;
        private int _retargetFadeOutFrames;
        private long _retargetGeneration;
        private int _retargetSelectionIndex;
        private int _endFadeOutFrames;
        private bool _usesEqualPowerCrossfade;
        private bool _hasBegunEndFade;
        private AttenuationSettings _attenuation = AttenuationSettings.None;
        private GameObjectParameterSource _gameObjectParameters;
        private bool _isPositioned;
        private byte _virtualQueueBehaviour;
        private byte _belowThresholdBehaviour;
        private bool _wasVirtualisedByLimit;
        private VoiceState _state;

        public Voice(EngineTelemetry telemetry) => _telemetry = telemetry;

        // Read from the control thread, so it stays keyed to the media reference rather than to the
        // state, which only the render thread touches.
        public bool IsActive => Volatile.Read(ref _media) != null;

        public PlayingId PlayingId => _playingId;
        public PostCompletionState PostCompletion => _postCompletion;
        public bool IsScheduledVoice => _isScheduledVoice;
        public uint OutputBusId => _parameters.OutputBusId;
        public bool IsVirtual => IsActive && _state == VoiceState.Virtualised;
        public bool IsOverLimitVirtual => IsVirtual && _wasVirtualisedByLimit;
        public bool IsStopping => IsActive && _state == VoiceState.Stopping;
        public ReadOnlyMemory<VoiceLimit> Limits => _limits;
        public GameObjectId GameObject => _gameObject;

        // Pausing removes a voice from the audible count, but it retains its slot so resuming cannot
        // overbook either the pool or its authored node limit.
        public bool IsSounding => IsActive && _state is not (VoiceState.Stopping or VoiceState.Paused or VoiceState.Virtualised);

        public bool OccupiesPhysicalVoiceBudget => IsActive && _state is not (VoiceState.Stopping or VoiceState.Virtualised);

        public bool OccupiesInstanceBudget => IsActive && _state != VoiceState.Stopping;

        public float Priority => _parameters.Priority;

        // Whether an action that names a node reaches this voice: the node it is playing, or any
        // node it hangs under. A zero target is an action that reaches everything.
        public bool IsUnder(uint targetNodeId)
        {
            if (targetNodeId == 0 || targetNodeId == _nodeId)
                return true;

            var ancestorNodeIds = _ancestorNodeIds.Span;
            for (var ancestorOrdinal = 0; ancestorOrdinal < ancestorNodeIds.Length; ancestorOrdinal++)
            {
                if (ancestorNodeIds[ancestorOrdinal] == targetNodeId)
                    return true;
            }
            return false;
        }

        public bool BelongsTo(GameObjectId gameObject) => _gameObject == gameObject;

        // Positions crossing this boundary are in mix frames, never source frames: the caller is
        // asking a question about time, and the source's own rate is the resampler's business.
        public void Activate(in VoiceStart start, long startOrdinal)
        {
            _playingId = start.PlayingId;
            _postCompletion = start.PostCompletion;
            _parameters = start.Parameters;
            _limits = start.Limits;
            _nodeId = start.NodeId;
            _gameObject = start.GameObject;
            _ancestorNodeIds = start.AncestorNodeIds;
            _startOrdinal = startOrdinal;
            _currentMixFrame = start.InitialMixFrame;
            _firstMixFrame = start.InitialMixFrame;
            _firstAbsoluteOutputFrame = -1;
            _remainingStartDelayFrames = Math.Max(0, start.StartDelayFrames);
            _remainingPlayCount = start.IsLooping ? 0 : Math.Max(0, start.LoopCount);
            _isLooping = _remainingPlayCount == 0 || _remainingPlayCount > 1;
            _isScheduledVoice = start.IsScheduledVoice;
            _wasStolen = false;
            _retargetFadeOutFrames = start.RetargetFadeOutFrames;
            _retargetGeneration = 0;
            _retargetSelectionIndex = -1;
            _endFadeOutFrames = start.EndFadeOutFrames;
            _usesEqualPowerCrossfade = start.UsesEqualPowerCrossfade;
            _hasBegunEndFade = false;
            _attenuation = start.Attenuation ?? AttenuationSettings.None;
            _gameObjectParameters = start.GameObjectParameters;
            _isPositioned = start.IsPositioned;
            _virtualQueueBehaviour = start.VirtualQueueBehaviour;
            _belowThresholdBehaviour = start.BelowThresholdBehaviour;
            _wasVirtualisedByLimit = false;

            _pitchRamp.SetImmediately(start.Parameters.PitchCents);
            _lowPassRamp.SetImmediately(start.Parameters.LowPassFilter);
            _highPassRamp.SetImmediately(start.Parameters.HighPassFilter);
            _resampler.Start(start.Media, 0d);
            _resampler.SetPitchCents(start.Parameters.PitchCents);
            _resampler.SeekTo(_resampler.ToSourceFrames(start.InitialMixFrame) + start.SourceFrameAlignment);
            _filter.Reset();
            _filter.Set(start.Parameters.LowPassFilter, start.Parameters.HighPassFilter);
            _volumeRamp.SetImmediately(start.Parameters.Volume);
            BeginStarting(start.InitialMixFrame, start.FadeInFrames, start.UsesEqualPowerCrossfade, start.ForceFadeIn);
            Volatile.Write(ref _media, start.Media);
        }

        public void ActivateVirtual(in VoiceStart start, long startOrdinal)
        {
            Activate(start, startOrdinal);
            _state = VoiceState.Virtualised;
            _wasVirtualisedByLimit = true;
            _gainRamp.SetImmediately(0f);
        }

        // Whether this voice counts towards the given node's instances. A limit node of zero is the
        // pool as a whole, which every voice counts towards.
        public bool IsCountedAgainst(in VoiceLimit requestedLimit, GameObjectId requestingGameObject)
        {
            foreach (var limit in _limits.Span)
            {
                if (limit.NodeId == requestedLimit.NodeId
                    && string.Equals(limit.BankPath, requestedLimit.BankPath, StringComparison.OrdinalIgnoreCase)
                    && (requestedLimit.Scope == VoiceLimitScope.Global || BelongsTo(requestingGameObject)))
                    return true;
            }
            return false;
        }

        public bool TryRetarget(
            PlayingId playingId,
            uint nodeId,
            SourceMedia media,
            in VoiceParameters parameters,
            int fadeInFrames,
            int fadeOutFrames,
            long generation,
            int selectionIndex)
        {
            if (!Matches(playingId) || _nodeId != nodeId || !ReferenceEquals(_media, media) || _retargetGeneration == generation)
                return false;

            // Every authored property the new branch carries moves to its new value rather than
            // jumping to it, and the voice keeps sounding through the move — which is the whole
            // point of retargeting rather than restarting.
            var rampFrames = fadeInFrames > 0 ? fadeInFrames : GainRamp.DeclickFrames;
            _parameters = parameters;
            _volumeRamp.RampTo(parameters.Volume, rampFrames);
            _pitchRamp.RampTo(parameters.PitchCents, rampFrames);
            _lowPassRamp.RampTo(parameters.LowPassFilter, rampFrames);
            _highPassRamp.RampTo(parameters.HighPassFilter, rampFrames);
            _retargetFadeOutFrames = fadeOutFrames;
            _retargetGeneration = generation;
            _retargetSelectionIndex = selectionIndex;
            return true;
        }

        public void StopUnlessRetargeted(PlayingId playingId, long generation)
        {
            if (!Matches(playingId) || _retargetGeneration == generation)
                return;
            BeginStopping(_retargetFadeOutFrames > 0 ? _retargetFadeOutFrames : GainRamp.DeclickFrames);
        }

        public bool WasRetargeted(PlayingId playingId, uint nodeId, SourceMedia media, long generation, int selectionIndex)
            => Matches(playingId)
                && _nodeId == nodeId
                && ReferenceEquals(_media, media)
                && _retargetGeneration == generation
                && _retargetSelectionIndex == selectionIndex;

        // Which of two voices gives way first: the less important one, and between two of equal
        // importance the one that has been sounding longest.
        public bool GivesWayBefore(Voice other)
            => Priority != other.Priority
                ? Priority < other.Priority
                : _startOrdinal < other._startOrdinal;

        // Starting anywhere but the first frame of the media means the transport put it there — a
        // seek, a scrub, a cue reconstructed part way through — and the jump from silence to
        // mid-waveform is the engine's to smooth. Starting on the first frame is the sound's own
        // beginning, and ramping that would soften an attack the author wrote.
        //
        // An authored fade is the exception, and the only one: the author asked for it, so it
        // replaces the de-click rather than being added to it.
        private void BeginStarting(long initialMixFrame, int fadeInFrames, bool usesEqualPowerCrossfade = false, bool forceFadeIn = false)
        {
            if (fadeInFrames > 0)
            {
                _state = VoiceState.Starting;
                _gainRamp.SetImmediately(0f);
                _gainRamp.RampTo(
                    1f,
                    fadeInFrames,
                    usesEqualPowerCrossfade ? GainRampCurve.EqualPower : GainRampCurve.Linear);
                return;
            }

            if (initialMixFrame == 0 && !forceFadeIn)
            {
                _state = VoiceState.Playing;
                _gainRamp.SetImmediately(1f);
                return;
            }

            _state = VoiceState.Starting;
            _gainRamp.SetImmediately(0f);
            _gainRamp.DeclickIn();
        }

        // Accumulates into the bus block and returns how many of its frames this voice reached. A
        // voice can only ever run out part way through a block, never join part way through one:
        // starts are applied at a block boundary, and a block is cut short wherever a cue is due.
        // That is what lets the caller read the return value as "frames that had audio in them".
        public int MixBlock(
            float[] busSamples,
            long firstAbsoluteOutputFrame,
            int frameCount,
            out bool hasEnded)
        {
            hasEnded = false;
            var media = Volatile.Read(ref _media);
            if (media == null)
                return 0;

            var sourceFrameCount = media.FrameCount;
            var loopStartFrame = _isLooping ? media.LoopStartFrame ?? 0 : 0;
            var loopEndFrame = _isLooping ? media.LoopEndFrame ?? sourceFrameCount : 0;
            AdvanceParameterRamps(frameCount);
            var spatialGain = ResolveSpatialParameters(out var emitterPosition, out var listenerPositions);
            ApplyThresholdBehaviour(spatialGain);
            for (var blockFrame = 0; blockFrame < frameCount; blockFrame++)
            {
                // An authored delay is counted in output frames rather than waited out anywhere
                // else: the action asked for the sound a fixed time after the event, and the audio
                // clock is the only clock that measures that exactly.
                if (_remainingStartDelayFrames > 0)
                {
                    _remainingStartDelayFrames--;
                    continue;
                }

                // A voice on its way out goes quiet over the ramp and only then gives up its slot.
                // A cancelled one reports nothing — the caller asked for it and already knows — but
                // a stolen one does, because nobody asked and its post would otherwise be waited on
                // for a sound that is never going to finish.
                if (_state == VoiceState.Stopping && !_gainRamp.IsRamping)
                {
                    hasEnded = _wasStolen;
                    Deactivate();
                    return blockFrame;
                }

                if (_state == VoiceState.Pausing && !_gainRamp.IsRamping)
                {
                    _state = VoiceState.Paused;
                    return blockFrame;
                }

                if (_state == VoiceState.Paused)
                    return blockFrame;

                if (_state == VoiceState.Virtualised)
                {
                    if (_resampler.SourceFramePosition >= (_isLooping ? loopEndFrame : sourceFrameCount))
                    {
                        if (CanWrap(loopEndFrame, loopStartFrame))
                            _resampler.SeekTo(loopStartFrame + (_resampler.SourceFramePosition - loopStartFrame) % (loopEndFrame - loopStartFrame));
                        else
                        {
                            hasEnded = true;
                            Deactivate();
                            return blockFrame;
                        }
                    }

                    if (_virtualQueueBehaviour == 1)
                        _resampler.Advance();
                    Interlocked.Increment(ref _currentMixFrame);
                    continue;
                }

                if (_resampler.SourceFramePosition >= (_isLooping ? loopEndFrame : sourceFrameCount))
                {
                    if (CanWrap(loopEndFrame, loopStartFrame))
                        _resampler.SeekTo(loopStartFrame + (_resampler.SourceFramePosition - loopStartFrame) % (loopEndFrame - loopStartFrame));
                    else
                    {
                        hasEnded = true;
                        Deactivate();
                        return blockFrame;
                    }
                }

                if (!_hasBegunEndFade
                    && _endFadeOutFrames > 0
                    && _resampler.OutputFramesUntil(sourceFrameCount) <= _endFadeOutFrames)
                {
                    _hasBegunEndFade = true;
                    _gainRamp.RampTo(
                        0f,
                        _endFadeOutFrames,
                        _usesEqualPowerCrossfade ? GainRampCurve.EqualPower : GainRampCurve.Linear);
                }

                if (_firstAbsoluteOutputFrame < 0)
                {
                    _firstMixFrame = _currentMixFrame;
                    Volatile.Write(ref _firstAbsoluteOutputFrame, firstAbsoluteOutputFrame + blockFrame);
                }

                var gain = _gainRamp.NextGain() * _volumeRamp.NextGain() * spatialGain;
                if (_state == VoiceState.Starting && !_gainRamp.IsRamping)
                    _state = VoiceState.Playing;

                _panner.ReadStereo(media, _resampler, loopStartFrame, loopEndFrame, out var left, out var right);
                if (_isPositioned)
                    Panner.ApplyPosition(emitterPosition, listenerPositions.Span, ref left, ref right);
                if (_filter.IsActive)
                {
                    left = _filter.Process(0, left);
                    right = _filter.Process(1, right);
                }
                var busSampleIndex = blockFrame * PlaybackFormat.ChannelCount;
                busSamples[busSampleIndex] += left * gain;
                busSamples[busSampleIndex + 1] += right * gain;

                _resampler.Advance();
                Interlocked.Increment(ref _currentMixFrame);
            }
            return frameCount;
        }

        // Pitch is pushed at the resampler only while it is actually moving, because setting it
        // rebuilds the kernel whenever the step changes. The filter cutoffs are read straight off
        // their ramps below, where the block already re-sets them for distance.
        private void AdvanceParameterRamps(int frameCount)
        {
            var isPitchRamping = _pitchRamp.IsRamping;
            var pitchCents = _pitchRamp.Advance(frameCount);
            if (isPitchRamping)
                _resampler.SetPitchCents(pitchCents);
            _lowPassRamp.Advance(frameCount);
            _highPassRamp.Advance(frameCount);
        }

        private float ResolveSpatialParameters(out Vector3 emitterPosition, out ReadOnlyMemory<Vector3> listenerPositions)
        {
            var gameObjectParameters = _gameObjectParameters?.Current ?? GameObjectParameters.Empty;
            emitterPosition = gameObjectParameters.Position;
            listenerPositions = gameObjectParameters.ListenerPositions;
            if (!_isPositioned || listenerPositions.IsEmpty)
            {
                _filter.Set(_lowPassRamp.Value, _highPassRamp.Value);
                return 1f;
            }

            var distance = float.MaxValue;
            foreach (var listenerPosition in listenerPositions.Span)
                distance = Math.Min(distance, Vector3.Distance(emitterPosition, listenerPosition));
            var volume = AudioLevel.DecibelsToLinear(_attenuation.VolumeDecibels(distance));
            _filter.Set(
                Math.Clamp(_lowPassRamp.Value + _attenuation.LowPassFilter(distance), 0f, 100f),
                Math.Clamp(_highPassRamp.Value + _attenuation.HighPassFilter(distance), 0f, 100f));
            return volume;
        }

        private void ApplyThresholdBehaviour(float spatialGain)
        {
            var isBelowThreshold = _parameters.Volume * spatialGain <= AudioLevel.DecibelsToLinear(AudioLevel.SilenceDecibels);
            if (!isBelowThreshold && _state == VoiceState.Virtualised && !_wasVirtualisedByLimit)
            {
                ReturnToPhysical();
                return;
            }
            if (!isBelowThreshold || _state is VoiceState.Virtualised or VoiceState.Stopping)
                return;

            if (_belowThresholdBehaviour == 1 || (_belowThresholdBehaviour == 3 && !_isLooping))
            {
                BeginStopping(GainRamp.DeclickFrames);
                _telemetry.CountThresholdKilledVoice();
            }
            else if (_belowThresholdBehaviour is 2 or 3)
            {
                _state = VoiceState.Virtualised;
                _wasVirtualisedByLimit = false;
                _gainRamp.SetImmediately(0f);
                _telemetry.CountVoiceThresholdTransition();
                _telemetry.CountThresholdVirtualisedVoice();
            }
        }

        public void ReturnToPhysical()
        {
            if (_state != VoiceState.Virtualised)
                return;
            if (_virtualQueueBehaviour == 0)
            {
                _resampler.SeekTo(0d);
                Interlocked.Exchange(ref _currentMixFrame, 0);
            }
            _state = VoiceState.Starting;
            _wasVirtualisedByLimit = false;
            _gainRamp.DeclickIn();
            _telemetry.CountVoiceThresholdTransition();
        }

        private bool CanWrap(int loopEndFrame, int loopStartFrame)
        {
            if (!_isLooping || loopEndFrame <= loopStartFrame)
                return false;
            if (_remainingPlayCount == 0)
                return true;
            if (_remainingPlayCount <= 1)
                return false;

            _remainingPlayCount--;
            return true;
        }

        public bool Matches(PlayingId playingId) => IsActive && _playingId == playingId;

        public void Cancel(PlayingId playingId)
        {
            if (!Matches(playingId))
                return;
            BeginStopping(GainRamp.DeclickFrames);
        }

        public void Pause(PlayingId playingId)
        {
            if (!Matches(playingId) || _isScheduledVoice || _state is VoiceState.Pausing or VoiceState.Paused)
                return;

            BeginPausing(GainRamp.DeclickFrames);
        }

        public void Resume(PlayingId playingId, long deviceOutputFrame)
        {
            if (!Matches(playingId) || _isScheduledVoice || _state is not (VoiceState.Pausing or VoiceState.Paused))
                return;

            ResetOutputEpoch(deviceOutputFrame);
            _state = VoiceState.Starting;
            _gainRamp.DeclickIn();
        }

        // What an event's Stop, Pause or Resume action does to this voice, if it reaches it.
        //
        // The fade is the action's own, so it is a level change the author wrote rather than the
        // de-click the transport owes: a two-second stop is a fade, and 3 ms is a repair.
        public bool ApplyAction(ResolvedActionKind actionKind, uint targetNodeId, GameObjectId gameObject, bool isScopedToGameObject, int fadeFrames, long deviceOutputFrame)
        {
            if (!IsActive || !IsUnder(targetNodeId))
                return false;
            if (isScopedToGameObject && !BelongsTo(gameObject))
                return false;

            switch (actionKind)
            {
                case ResolvedActionKind.Stop:
                    if (_state == VoiceState.Stopping)
                        return false;
                    BeginStopping(fadeFrames > 0 ? fadeFrames : GainRamp.DeclickFrames);
                    return true;

                case ResolvedActionKind.Pause:
                    if (_state is VoiceState.Pausing or VoiceState.Paused or VoiceState.Stopping)
                        return false;
                    BeginPausing(fadeFrames > 0 ? fadeFrames : GainRamp.DeclickFrames);
                    return true;

                case ResolvedActionKind.Resume:
                    if (_state is not (VoiceState.Pausing or VoiceState.Paused))
                        return false;
                    ResetOutputEpoch(deviceOutputFrame);
                    _state = VoiceState.Starting;
                    _gainRamp.RampTo(1f, fadeFrames > 0 ? fadeFrames : GainRamp.DeclickFrames);
                    return true;

                default:
                    return false;
            }
        }

        // Gives up its place to a voice that outranks it. Ramped out rather than cut, which is why
        // the pool holds more slots than it lets sound at once.
        public void Steal()
        {
            if (_state == VoiceState.Stopping)
                return;
            _wasStolen = true;
            BeginStopping(GainRamp.DeclickFrames);
        }

        // A voice already on its way out can still be asked to leave sooner, and that matters now
        // that not every ramp out is the same length: a seek fades its old position over 20 ms, and
        // a stop arriving during that must not have to wait out the seek's ramp before the voice
        // goes quiet. Never longer, though -- lengthening a ramp already in progress would let a
        // cancelled voice outlive the thing that cancelled it.
        private void BeginStopping(int rampFrames)
        {
            if (_state == VoiceState.Stopping)
            {
                if (_gainRamp.IsRamping && rampFrames < _gainRamp.RemainingFrames)
                    _gainRamp.RampTo(0f, rampFrames);
                return;
            }

            _state = VoiceState.Stopping;
            _remainingStartDelayFrames = 0;
            _gainRamp.RampTo(0f, rampFrames);
        }

        private void BeginPausing(int rampFrames)
        {
            _state = VoiceState.Pausing;
            _gainRamp.RampTo(0f, rampFrames);
        }

        // Puts the voice back on the device clock after the transport was frozen.
        //
        // The device drains throughout a pause, so by the time playback resumes those frames are
        // counted against this voice and its position is that much too far along — far enough that
        // it stops tracking the device at all and falls back to how far the voice has read, which
        // only moves when a buffer is rendered. That is seen as a playhead jumping a bufferful at a
        // time instead of gliding.
        //
        // Re-anchoring on the frame the device has actually reached, and on the position that is
        // audible at that moment, starts the sum again from something true. The audible position is
        // worked out with the old anchor before it is replaced, because that is the last moment it
        // still means anything.
        public void ResetOutputEpoch(long deviceOutputFrame)
        {
            if (!IsActive || !TryGetPosition(_playingId, deviceOutputFrame, out var audibleMixFrame))
                return;

            _firstMixFrame = audibleMixFrame;
            Volatile.Write(ref _firstAbsoluteOutputFrame, deviceOutputFrame);
        }

        public void DeactivateIfScheduled()
        {
            if (_isScheduledVoice)
                Deactivate();
        }

        public void FadeOutIfScheduled(int rampFrames = GainRamp.DeclickFrames)
        {
            if (_isScheduledVoice)
                BeginStopping(rampFrames);
        }

        public void DeactivateIfFinishedScheduledFade()
        {
            if (_isScheduledVoice && _state == VoiceState.Stopping && !_gainRamp.IsRamping)
                Deactivate();
        }

        public bool TryCreateSeekStart(PlayingId playingId, long mixFrame, out VoiceStart start)
        {
            start = default;
            var media = Volatile.Read(ref _media);
            if (media == null || !Matches(playingId) || _isScheduledVoice)
                return false;
            var seekedMixFrame = Math.Max(0, mixFrame);

            // Land where the waveform continues rather than where it restarts. The requested
            // frame is still what gets reported; this moves only what is read, by less than a
            // period, which removes the phase difference the cross-fade would otherwise slew.
            var requestedSourceFrame = _resampler.ToSourceFrames(seekedMixFrame);
            var alignment = SeekAlignment.AlignToWaveform(
                media.Samples,
                media.ChannelCount,
                _resampler.SourceFramePosition,
                requestedSourceFrame) - requestedSourceFrame;

            start = new VoiceStart(
                _playingId,
                _postCompletion,
                media,
                _parameters,
                _limits.IsEmpty ? VoiceLimit.None : _limits.Span[^1],
                _limits,
                _nodeId,
                _gameObject,
                _ancestorNodeIds,
                seekedMixFrame,
                StartDelayFrames: 0,
                FadeInFrames: GainRamp.SeekCrossFadeFrames,
                IsLooping: _remainingPlayCount == 0,
                IsScheduledVoice: false,
                _retargetFadeOutFrames,
                _endFadeOutFrames,
                _usesEqualPowerCrossfade,
                _remainingPlayCount,
                _attenuation,
                _gameObjectParameters,
                _isPositioned,
                _virtualQueueBehaviour,
                _belowThresholdBehaviour,
                ForceFadeIn: true,
                SourceFrameAlignment: alignment);
            BeginStopping(GainRamp.SeekCrossFadeFrames);
            return true;
        }

        public void Deactivate()
        {
            _state = VoiceState.Idle;
            _wasStolen = false;
            _remainingStartDelayFrames = 0;
            _gainRamp.SetImmediately(1f);
            _ancestorNodeIds = default;
            _nodeId = 0;
            _gameObject = default;
            Volatile.Write(ref _media, null);
            _postCompletion = null;
        }

        // Reported in mix frames, so the caller can turn it straight into a time without knowing
        // what rate the source was authored at.
        public bool TryGetPosition(PlayingId playingId, long? absoluteOutputFrame, out long mixFrame)
        {
            mixFrame = 0;
            if (!Matches(playingId))
                return false;

            var media = Volatile.Read(ref _media);
            var firstAbsoluteOutputFrame = Interlocked.Read(ref _firstAbsoluteOutputFrame);
            if (!absoluteOutputFrame.HasValue || firstAbsoluteOutputFrame < 0 || media == null)
            {
                mixFrame = Interlocked.Read(ref _currentMixFrame);
                return true;
            }

            var audibleFrameCount = Math.Max(0, absoluteOutputFrame.Value - firstAbsoluteOutputFrame);
            var mixFrameCount = PlaybackTime.ToFrames(media.Duration);

            // The device clock goes on running while the transport is frozen, so counting output
            // frames alone would walk the playhead past the last frame the voice actually read.
            mixFrame = Math.Min(_firstMixFrame + audibleFrameCount, Interlocked.Read(ref _currentMixFrame));
            if (_isLooping && mixFrameCount > 0)
                mixFrame %= mixFrameCount;
            else
                mixFrame = Math.Min(mixFrame, mixFrameCount);
            return true;
        }
    }
}
