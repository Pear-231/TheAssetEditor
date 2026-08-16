using System.Diagnostics;
using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Cache;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;

namespace Editors.AnimationMeta.SuperView
{
    // Keeps a sound engine timeline aligned with an animation: rebuilds it whenever the
    // animation or its sound triggers change, drives the animation clock from the audio
    // device while it plays, and follows the player through play, pause and stop.
    internal sealed class AnimationAudioTimeline(ISoundEngine soundEngine, SceneObject animatedObject)
    {
        // A sound the animation asks to be heard at a fixed point on its timeline.
        internal readonly record struct AudioCue(string Name, TimeSpan Position, SoundEngineCacheEntry Audio);

        // Everything about a scheduled sound that would require the timeline to be rebuilt.
        // The audio is identified by content, so re-resolving to the same sound is not a change.
        private readonly record struct AudioCueKey(long PositionTicks, string ContentHash);

        // A cue watched over one pass of the timeline so the point it is actually heard at
        // can be reported against the point the animation meta asked for.
        private sealed class TrackedCue(string name, TimeSpan position, TimeSpan duration, VoiceHandle voiceHandle)
        {
            public string Name { get; set; } = name;
            public TimeSpan Position { get; } = position;
            public TimeSpan Duration { get; } = duration;
            public VoiceHandle VoiceHandle { get; } = voiceHandle;
            public bool WasHeard { get; private set; }
            public bool WasReportedUnheard { get; private set; }

            public void MarkHeard() => WasHeard = true;
            public void MarkReportedUnheard() => WasReportedUnheard = true;

            // Cues already behind the position the timeline restarts from are not expected
            // to be heard on this pass, so they are neither reported nor missed.
            public void BeginPassAt(TimeSpan timelinePosition, TimeSpan tolerance)
            {
                WasHeard = Position + tolerance < timelinePosition;
                WasReportedUnheard = false;
            }
        }

        // Playback and the animation share a clock, so a difference this wide is a disturbance
        // worth reporting - a stalled pump, or a timeline built from a length that no longer
        // matches the animation - rather than the jitter of reading two clocks a moment apart.
        private static readonly TimeSpan s_maximumSynchronisationError = TimeSpan.FromMilliseconds(100);

        // How often the running relationship between the two clocks is written out. Often
        // enough to watch playback, rare enough not to bury the cue timings.
        private static readonly TimeSpan s_synchronisationSampleInterval = TimeSpan.FromSeconds(1);

        private readonly ILogger _logger = Logging.Create<AnimationAudioTimeline>();
        private long _playbackGeneration;
        private AnimationClip? _animation;
        private TimeSpan _animationLength;
        private bool _isLooping;
        private AudioCueKey[] _audioCueKeys = [];
        private TrackedCue[] _trackedCues = [];
        private TimeSpan _lastObservedAudioPosition;
        private long _lastObservedGeneration;
        private long _lastSynchronisationSampleTimestamp;
        private bool _isOutOfSynchronisation;
        private long _outOfSynchronisationSince;
        private TimeSpan _peakSynchronisationError;
        private bool _wasDrivenByAudio;

        private AnimationPlayer Player => animatedObject.Player;

        // Another editor taking the engine over silently invalidates this timeline.
        public bool IsCurrent => _playbackGeneration != 0 && soundEngine.PlaybackGeneration == _playbackGeneration;

        // Whether the animation clock is following audio rather than free-running on the
        // frame delta, which is the condition every cue timing below is measured under.
        private bool IsDrivenByAudio => IsCurrent && soundEngine.PlaybackState == SoundPlaybackState.Playing;

        // The animation's length and whether it loops are built into the scheduled timeline,
        // so a change to either leaves what is scheduled describing an animation that no
        // longer exists. Neither raises an event, so both are left to be noticed.
        public bool NeedsResynchronisation
            => IsCurrent
            && (_isLooping != Player.LoopAnimation || _animationLength != Player.GetAnimationLength());

        public void Synchronise(IReadOnlyList<AudioCue> audioCues)
        {
            var animation = Player.AnimationClip;
            if (animation == null || audioCues.Count == 0)
            {
                StopAndRelease();
                return;
            }

            var animationLength = Player.GetAnimationLength();
            var isLooping = Player.LoopAnimation;
            var audioCueKeys = audioCues.Select(ToKey).ToArray();
            if (IsCurrent
                && ReferenceEquals(_animation, animation)
                && _animationLength == animationLength
                && _isLooping == isLooping
                && _audioCueKeys.SequenceEqual(audioCueKeys))
            {
                // The same sounds at the same points, so only the names they are reported
                // under can have changed.
                for (var cueIndex = 0; cueIndex < _trackedCues.Length; cueIndex++)
                    _trackedCues[cueIndex].Name = audioCues[cueIndex].Name;
                return;
            }

            _playbackGeneration = soundEngine.CreateTimeline(animationLength, isLooping);
            _animation = animation;
            _animationLength = animationLength;
            _isLooping = isLooping;
            _audioCueKeys = audioCueKeys;

            _trackedCues = audioCues
                .Select(audioCue => new TrackedCue(
                    audioCue.Name,
                    audioCue.Position,
                    GetDuration(audioCue.Audio),
                    soundEngine.Schedule(audioCue.Audio, audioCue.Position, _playbackGeneration)))
                .ToArray();
            _lastObservedAudioPosition = TimeSpan.Zero;

            var playbackGeneration = _playbackGeneration;
            Player.PositionSource = () =>
                soundEngine.PlaybackState == SoundPlaybackState.Playing
                && soundEngine.PlaybackGeneration == playbackGeneration
                    ? soundEngine.Position
                    : null;

            _logger.Here().Information(
                $"Prepared animation audio timeline {_playbackGeneration}: {audioCues.Count} audio cues " +
                $"over {animationLength.TotalMilliseconds:F2} ms, {Player.FrameCount()} frames at {Player.GetFps()} fps, looping {isLooping}");
            foreach (var trackedCue in _trackedCues)
                _logger.Here().Information(
                    $"Timeline {_playbackGeneration} scheduled '{trackedCue.Name}' at {trackedCue.Position.TotalMilliseconds:F2} ms " +
                    $"({trackedCue.Duration.TotalMilliseconds:F2} ms long, ending at {(trackedCue.Position + trackedCue.Duration).TotalMilliseconds:F2} ms)");

            if (Player.IsPlaying)
                StartAudio();
        }

        public void OnAnimationPlaybackChanged(bool isPlaying)
        {
            if (isPlaying)
            {
                if (!IsCurrent)
                    return;
                if (soundEngine.PlaybackState == SoundPlaybackState.Paused)
                {
                    soundEngine.Resume();
                    _logger.Here().Information(
                        $"Resumed timeline {_playbackGeneration} at {soundEngine.Position.TotalMilliseconds:F2} ms, " +
                        $"animation at {Player.GetTime().TotalMilliseconds:F2} ms (frame {Player.CurrentFrame})");
                }
                else
                    StartAudio();
                return;
            }

            if (!Player.IsEnabled || IsAtEndOfNonLoopingAnimation())
            {
                if (IsCurrent)
                    _logger.Here().Information(
                        $"Stopped timeline {_playbackGeneration} at {soundEngine.Position.TotalMilliseconds:F2} ms, " +
                        $"animation at {Player.GetTime().TotalMilliseconds:F2} ms (frame {Player.CurrentFrame})");
                StopAndRelease();
            }
            else if (IsCurrent && soundEngine.PlaybackState == SoundPlaybackState.Playing)
            {
                soundEngine.Pause();
                _logger.Here().Information(
                    $"Paused timeline {_playbackGeneration} at {soundEngine.Position.TotalMilliseconds:F2} ms, " +
                    $"animation at {Player.GetTime().TotalMilliseconds:F2} ms (frame {Player.CurrentFrame})");
            }
        }

        public void OnAnimationFrameChanged()
        {
            if (!Player.IsEnabled)
                return;

            // Reported before the timeline drops out, so that losing the engine is itself
            // recorded rather than being the reason nothing more is heard from here.
            LogClockSourceHandover();
            if (!IsCurrent)
                return;

            var animationPosition = Player.GetTime();
            var audioPosition = soundEngine.Position;
            ObserveAudioCues(animationPosition, audioPosition);
            LogSynchronisation(animationPosition, audioPosition);
        }

        public void StopAndRelease()
        {
            if (IsCurrent)
                soundEngine.Stop();
            Release();
        }

        // Leaves the sound engine alone, for when something else has already taken it over.
        public void Release()
        {
            _playbackGeneration = 0;
            _animation = null;
            _animationLength = TimeSpan.Zero;
            _isLooping = false;
            _audioCueKeys = [];
            _trackedCues = [];
            _lastObservedAudioPosition = TimeSpan.Zero;
            _lastObservedGeneration = 0;
            _isOutOfSynchronisation = false;
            _outOfSynchronisationSince = 0;
            _peakSynchronisationError = TimeSpan.Zero;
            _wasDrivenByAudio = false;
            Player.PositionSource = null;
        }

        private void StartAudio()
        {
            var animationPosition = Player.GetTime();
            BeginCuePass(animationPosition);
            soundEngine.Start(_playbackGeneration, animationPosition);
            _logger.Here().Information(
                $"Started timeline {_playbackGeneration} from {animationPosition.TotalMilliseconds:F2} ms " +
                $"(animation frame {Player.CurrentFrame} of {Player.FrameCount()})");
        }

        // Reports the point on the timeline each cue was actually heard at. A voice only
        // reports a position while it is sounding, so the point it started is the audible
        // timeline position less how far into the sound the voice has read.
        private void ObserveAudioCues(TimeSpan animationPosition, TimeSpan audioPosition)
        {
            if (_trackedCues.Length == 0)
                return;

            // Looping and rewinding move the timeline back behind the cues already passed, so
            // the pass simply starts again. Jumping forward instead means the device played on
            // while nothing was watching, and the cues in between can no longer be accounted
            // for - which is worth saying, rather than quietly writing them off.
            var timelineJump = audioPosition - _lastObservedAudioPosition;
            if (timelineJump < TimeSpan.Zero)
                BeginCuePass(audioPosition);
            else if (timelineJump > s_maximumSynchronisationError)
            {
                if (_lastObservedGeneration == _playbackGeneration)
                    LogObservationGap(_lastObservedAudioPosition, audioPosition);
                BeginCuePass(audioPosition);
            }
            _lastObservedAudioPosition = audioPosition;
            _lastObservedGeneration = _playbackGeneration;

            if (!IsDrivenByAudio)
                return;

            foreach (var trackedCue in _trackedCues)
            {
                if (trackedCue.WasHeard)
                    continue;

                // The mixer renders up to an output buffer ahead of what can be heard, and a
                // voice reports zero until its first sample has reached the device. Reading
                // it before then would place every onset a whole buffer early.
                var voicePosition = soundEngine.GetPosition(trackedCue.VoiceHandle);
                if (voicePosition == null || voicePosition.Value <= TimeSpan.Zero)
                {
                    LogCueIfUnheard(trackedCue, audioPosition);
                    continue;
                }

                trackedCue.MarkHeard();
                var heardAt = audioPosition - voicePosition.Value;
                _logger.Here().Information(
                    $"Heard '{trackedCue.Name}' on timeline {_playbackGeneration}: scheduled for {trackedCue.Position.TotalMilliseconds:F2} ms, " +
                    $"started at {heardAt.TotalMilliseconds:F2} ms, error {(heardAt - trackedCue.Position).TotalMilliseconds:+0.00;-0.00;0.00} ms " +
                    $"(audio {audioPosition.TotalMilliseconds:F2} ms, animation {animationPosition.TotalMilliseconds:F2} ms, frame {Player.CurrentFrame})");
            }
        }

        // The whole of a cue passing without its voice ever being seen sounding means it
        // was either never played or was too short to catch between two animation frames.
        private void LogCueIfUnheard(TrackedCue trackedCue, TimeSpan audioPosition)
        {
            if (trackedCue.WasReportedUnheard
                || trackedCue.Duration <= TimeSpan.Zero
                || audioPosition <= trackedCue.Position + trackedCue.Duration)
                return;

            trackedCue.MarkReportedUnheard();
            _logger.Here().Warning(
                $"'{trackedCue.Name}' on timeline {_playbackGeneration} was scheduled for {trackedCue.Position.TotalMilliseconds:F2} ms " +
                $"but was never observed sounding before {audioPosition.TotalMilliseconds:F2} ms");
        }

        // A cue inside the gap was either heard unobserved or never played at all, and nothing
        // that follows can tell the two apart, so it is named here rather than reported later
        // as heard on the strength of having been skipped over.
        private void LogObservationGap(TimeSpan fromPosition, TimeSpan toPosition)
        {
            var skippedCues = _trackedCues
                .Where(trackedCue => !trackedCue.WasHeard
                    && trackedCue.Position >= fromPosition
                    && trackedCue.Position < toPosition)
                .Select(trackedCue => trackedCue.Name)
                .ToArray();

            _logger.Here().Warning(
                $"Timeline {_playbackGeneration} could not have observed anything between " +
                $"{fromPosition.TotalMilliseconds:F2} ms and {toPosition.TotalMilliseconds:F2} ms" +
                (skippedCues.Length == 0
                    ? ", which no cue fell inside"
                    : $", so {string.Join(", ", skippedCues.Select(name => $"'{name}'"))} cannot be accounted for"));
        }

        private void BeginCuePass(TimeSpan timelinePosition)
        {
            foreach (var trackedCue in _trackedCues)
                trackedCue.BeginPassAt(timelinePosition, s_maximumSynchronisationError);
        }

        // The animation follows the audio device while a timeline is playing and the render
        // clock otherwise. Which one is driving decides what every position reported here
        // means, so the switch is recorded as it happens rather than left to be inferred
        // from the next periodic sample.
        private void LogClockSourceHandover()
        {
            var isDrivenByAudio = IsDrivenByAudio;
            if (isDrivenByAudio == _wasDrivenByAudio)
                return;
            _wasDrivenByAudio = isDrivenByAudio;

            if (isDrivenByAudio)
            {
                _logger.Here().Information(
                    $"Animation timeline {_playbackGeneration} is following the audio clock from " +
                    $"{Player.GetTime().TotalMilliseconds:F2} ms (audio {soundEngine.Position.TotalMilliseconds:F2} ms, frame {Player.CurrentFrame})");
                return;
            }

            // Once the engine has moved on its position belongs to whoever took it, so only
            // the animation's own position is worth reporting here.
            var cause = IsCurrent
                ? $"the engine is {soundEngine.PlaybackState}"
                : "the sound engine was taken over";
            _logger.Here().Warning(
                $"Animation timeline {_playbackGeneration} left the audio clock at {Player.GetTime().TotalMilliseconds:F2} ms " +
                $"(frame {Player.CurrentFrame}) and is free-running on the render clock because {cause}");
        }

        // Reports how far apart the two timelines are without acting on it. Seeking the device
        // back would tear down and rebuild every sounding voice to chase a difference that,
        // while audio drives the animation clock, is only the gap between two reads of it.
        // Divergence is reported as it starts and ends; the rest is a periodic sample.
        private void LogSynchronisation(TimeSpan animationPosition, TimeSpan audioPosition)
        {
            var synchronisationError = animationPosition - audioPosition;
            if (_isLooping && _animationLength > TimeSpan.Zero)
                synchronisationError = WrapTimelineDifference(synchronisationError, _animationLength);

            var isOutOfSynchronisation = synchronisationError.Duration() > s_maximumSynchronisationError;
            if (isOutOfSynchronisation && !_isOutOfSynchronisation)
            {
                _outOfSynchronisationSince = Stopwatch.GetTimestamp();
                _peakSynchronisationError = synchronisationError;
                // Each position is reported against the length it wraps at, because the two
                // wrapping at different points is one of the ways they come to disagree.
                _logger.Here().Warning(
                    $"Audio and animation timeline {_playbackGeneration} diverged by {synchronisationError.TotalMilliseconds:+0.00;-0.00;0.00} ms " +
                    $"(animation {animationPosition.TotalMilliseconds:F2} ms of {Player.GetAnimationLength().TotalMilliseconds:F2} ms, " +
                    $"audio {audioPosition.TotalMilliseconds:F2} ms of {_animationLength.TotalMilliseconds:F2} ms, frame {Player.CurrentFrame})");
            }
            else if (isOutOfSynchronisation)
            {
                if (synchronisationError.Duration() > _peakSynchronisationError.Duration())
                    _peakSynchronisationError = synchronisationError;
            }
            else if (_isOutOfSynchronisation)
            {
                _logger.Here().Warning(
                    $"Audio and animation timeline {_playbackGeneration} came back within {s_maximumSynchronisationError.TotalMilliseconds:F0} ms " +
                    $"after {Stopwatch.GetElapsedTime(_outOfSynchronisationSince).TotalMilliseconds:F2} ms, " +
                    $"peaking at {_peakSynchronisationError.TotalMilliseconds:+0.00;-0.00;0.00} ms");
            }
            _isOutOfSynchronisation = isOutOfSynchronisation;

            if (Stopwatch.GetElapsedTime(_lastSynchronisationSampleTimestamp) < s_synchronisationSampleInterval)
                return;
            _lastSynchronisationSampleTimestamp = Stopwatch.GetTimestamp();

            _logger.Here().Information(
                $"Timeline {_playbackGeneration}: animation at {animationPosition.TotalMilliseconds:F2} ms (frame {Player.CurrentFrame}), " +
                $"audio at {audioPosition.TotalMilliseconds:F2} ms, difference {synchronisationError.TotalMilliseconds:+0.00;-0.00;0.00} ms, " +
                $"engine {soundEngine.PlaybackState}, animation driven by audio {IsDrivenByAudio}");
        }

        private bool IsAtEndOfNonLoopingAnimation()
        {
            var animationLengthUs = Player.GetAnimationLengthUs();
            return !Player.LoopAnimation
                && animationLengthUs > 0
                && Player.GetTimeUs() >= animationLengthUs;
        }

        private static AudioCueKey ToKey(AudioCue audioCue)
            => new(audioCue.Position.Ticks, audioCue.Audio.ContentHash);

        private static TimeSpan GetDuration(SoundEngineCacheEntry audio)
            => audio.Audio.SampleRate == 0
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(audio.Audio.SampleCount / (double)audio.Audio.SampleRate);

        private static TimeSpan WrapTimelineDifference(TimeSpan difference, TimeSpan timelineLength)
        {
            var halfLengthTicks = timelineLength.Ticks / 2;
            var differenceTicks = difference.Ticks;
            if (differenceTicks > halfLengthTicks)
                differenceTicks -= timelineLength.Ticks;
            else if (differenceTicks < -halfLengthTicks)
                differenceTicks += timelineLength.Ticks;
            return TimeSpan.FromTicks(differenceTicks);
        }
    }
}
