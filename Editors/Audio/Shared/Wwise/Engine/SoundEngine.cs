using Editors.Audio.Shared.Wwise.Engine.Cache;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Output;
using Editors.Audio.Shared.Wwise.Engine.Timing;

namespace Editors.Audio.Shared.Wwise.Engine
{
    // TODO: Add support for other wwise settings e.g. delays, volume etc.
    // TODO: Add Wwise container playback semantics (random, sequence, switch, and nested containers) instead of resolving only a single leaf sound.
    // TODO: Add attenuation e.g. in Super View
    // TODO: Add Seek e.g. for moving the playhead of waveform visualisations.
    public interface ISoundEngine : IDisposable
    {
        SoundPlaybackState PlaybackState { get; }
        TimeSpan Position { get; }
        long PlaybackGeneration { get; }
        VoiceHandle Play(SoundEngineCacheEntry audio, bool shouldLoop = false, TimeSpan? startPosition = null);
        long CreateTimeline(TimeSpan timelineDuration, bool shouldLoop);
        VoiceHandle Schedule(SoundEngineCacheEntry audio, TimeSpan timelinePosition, long playbackGeneration, bool shouldLoop = false);
        void Start(long playbackGeneration, TimeSpan? playbackPosition = null);
        void Pause();
        void Resume();
        void Stop();
        void Cancel(VoiceHandle voiceHandle);
        TimeSpan? GetPosition(VoiceHandle voiceHandle);
        event Action<VoiceHandle> VoiceCompleted;
        event Action<long> PlaybackCompleted;
    }

    public sealed class SoundEngine : ISoundEngine
    {
        // Frequent enough that playback completion is reported without an audible gap, but
        // far cheaper than polling every output callback.
        private const int PlaybackCompletionPollIntervalMilliseconds = 25;

        private readonly AudioMixer _audioMixer;
        private readonly IAudioOutputDevice _audioOutputDevice;
        private readonly PlaybackPositionTracker _playbackPositionTracker;
        private readonly Timer _playbackCompletionTimer;
        private long _nextVoiceIdentifier;
        private long _playbackGeneration;
        private long _lastReportedCompletedGeneration;
        private int _requestedPlaybackState = (int)SoundPlaybackState.Stopped;
        private int _isCompletionPollActive;
        private volatile bool _isDisposed;

        internal SoundEngine(
            IAudioOutputDevice audioOutputDevice)
        {
            _audioMixer = new AudioMixer();
            _audioOutputDevice = audioOutputDevice;
            _playbackPositionTracker = new PlaybackPositionTracker(_audioMixer, _audioOutputDevice);
            _playbackCompletionTimer = new Timer(PollPlaybackCompletion, null, Timeout.Infinite, Timeout.Infinite);
        }

        public TimeSpan Position => PlaybackTime.FromFrames(_playbackPositionTracker.GetAudiblePositionFrames(PlaybackState, PlaybackGeneration, _playbackPositionTracker.CaptureAbsoluteOutputFrame()));
        public long PlaybackGeneration => Interlocked.Read(ref _playbackGeneration);
        public SoundPlaybackState PlaybackState => (SoundPlaybackState)Volatile.Read(ref _requestedPlaybackState);
        public event Action<VoiceHandle> VoiceCompleted;
        public event Action<long> PlaybackCompleted;

        public VoiceHandle Play(SoundEngineCacheEntry audio, bool shouldLoop = false, TimeSpan? startPosition = null)
        {
            ThrowIfDisposed();
            var pcmData = GetPcmData(audio);
            EnsureOutputDeviceCreated();
            var isStartingPlayback = PlaybackState == SoundPlaybackState.Stopped;
            var playbackGeneration = isStartingPlayback
                ? Interlocked.Increment(ref _playbackGeneration)
                : PlaybackGeneration;
            var voiceIdentifier = Interlocked.Increment(ref _nextVoiceIdentifier);
            var startPositionFrame = PlaybackTime.ToFrames(startPosition ?? TimeSpan.Zero);
            Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Playing);

            var commandSequence = _audioMixer.SubmitCommand(MixerCommand.Play(
                playbackGeneration,
                voiceIdentifier,
                pcmData,
                startPositionFrame,
                shouldLoop));
            if (isStartingPlayback)
                _playbackPositionTracker.SetPendingPosition(startPositionFrame, commandSequence);

            StartOutputAndAnchorPosition();
            SchedulePlaybackCompletionPoll();
            return new VoiceHandle(voiceIdentifier, playbackGeneration);
        }

        public long CreateTimeline(TimeSpan timelineDuration, bool shouldLoop)
        {
            ThrowIfDisposed();
            var playbackGeneration = Interlocked.Increment(ref _playbackGeneration);
            var commandSequence = _audioMixer.SubmitCommand(MixerCommand.CreateTimeline(
                playbackGeneration,
                PlaybackTime.ToFrames(timelineDuration),
                shouldLoop));
            _playbackPositionTracker.SetPendingPosition(0, commandSequence);
            ProcessCommandsWithoutOutputDevice();
            Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Stopped);
            return playbackGeneration;
        }

        public VoiceHandle Schedule(SoundEngineCacheEntry audio, TimeSpan timelinePosition, long playbackGeneration, bool shouldLoop = false)
        {
            ThrowIfDisposed();
            var voiceIdentifier = Interlocked.Increment(ref _nextVoiceIdentifier);
            if (playbackGeneration != PlaybackGeneration)
                return new VoiceHandle(voiceIdentifier, playbackGeneration);
            var pcmData = GetPcmData(audio);
            _audioMixer.SubmitCommand(MixerCommand.Schedule(
                playbackGeneration,
                voiceIdentifier,
                pcmData,
                PlaybackTime.ToFrames(timelinePosition),
                shouldLoop));

            ProcessCommandsWithoutOutputDevice();
            return new VoiceHandle(voiceIdentifier, playbackGeneration);
        }

        public void Start(long playbackGeneration, TimeSpan? playbackPosition = null)
        {
            ThrowIfDisposed();
            if (playbackGeneration != PlaybackGeneration)
                return;
            EnsureOutputDeviceCreated();
            Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Playing);
            var playbackPositionFrame = PlaybackTime.ToFrames(playbackPosition ?? TimeSpan.Zero);
            var commandSequence = _audioMixer.SubmitCommand(MixerCommand.Start(playbackGeneration, playbackPositionFrame));
            _playbackPositionTracker.SetPendingPosition(playbackPositionFrame, commandSequence);
            StartOutputAndAnchorPosition();
            SchedulePlaybackCompletionPoll();
        }

        public void Pause()
        {
            if (_isDisposed || PlaybackState != SoundPlaybackState.Playing)
                return;
            var audiblePlaybackFrame = _playbackPositionTracker.GetAudiblePositionFrames(PlaybackState, PlaybackGeneration, _playbackPositionTracker.CaptureAbsoluteOutputFrame());
            _audioMixer.SilenceImmediately();
            Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Paused);
            _audioMixer.SubmitCommand(MixerCommand.Pause());
            var commandSequence = _audioMixer.SubmitCommand(MixerCommand.Seek(PlaybackGeneration, audiblePlaybackFrame));
            _playbackPositionTracker.SetPendingPosition(audiblePlaybackFrame, commandSequence);
            FlushPhysicalOutputAndApplyPendingCommands();
        }

        public void Resume()
        {
            ThrowIfDisposed();
            if (PlaybackState != SoundPlaybackState.Paused)
                return;
            EnsureOutputDeviceCreated();
            Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Playing);
            _audioMixer.SubmitCommand(MixerCommand.Resume());
            StartOutputAndAnchorPosition();
            SchedulePlaybackCompletionPoll();
        }

        public void Stop()
        {
            if (_isDisposed)
                return;
            _audioMixer.SilenceImmediately();
            Interlocked.Increment(ref _playbackGeneration);
            Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Stopped);
            var commandSequence = _audioMixer.SubmitCommand(MixerCommand.Stop());
            _playbackPositionTracker.SetPendingPosition(0, commandSequence);
            FlushPhysicalOutputAndApplyPendingCommands();
        }

        public void Cancel(VoiceHandle voiceHandle)
            => CancelVoice(voiceHandle.VoiceIdentifier, voiceHandle.PlaybackGeneration);

        public TimeSpan? GetPosition(VoiceHandle voiceHandle)
        {
            if (voiceHandle == default || voiceHandle.PlaybackGeneration != PlaybackGeneration)
                return null;
            return _audioMixer.TryGetVoicePosition(
                voiceHandle,
                _playbackPositionTracker.CaptureAbsoluteOutputFrame(),
                out var pcmFrame)
                ? PlaybackTime.FromFrames(pcmFrame)
                : null;
        }

        private void CancelVoice(long voiceIdentifier, long playbackGeneration)
        {
            ThrowIfDisposed();
            _audioMixer.SubmitCommand(MixerCommand.Cancel(playbackGeneration, voiceIdentifier));

            // Cancelling the last audible voice leaves the output device running on a buffer
            // that still holds the audio rendered just before the cut. A late or starved
            // callback repeats that buffer instead of the silence that replaced it, which is
            // heard as a burst of buzzing, so take the same stop path Stop does. While
            // anything else is still audible the device has to keep rendering, and the
            // surviving voices supply the buffer that would otherwise be repeated.
            if (_audioMixer.HasAudibleContentOtherThan(voiceIdentifier, playbackGeneration))
                ProcessCommandsWithoutOutputDevice();
            else
                Stop();
        }

        private void EnsureOutputDeviceCreated()
        {
            if (_audioOutputDevice.EnsureCreated(_audioMixer))
                _playbackPositionTracker.ResetOutputEpoch();
        }

        // Restarting a stopped output client resets its clock, so the epoch has to be anchored after the restart, not before.
        private void StartOutputAndAnchorPosition()
        {
            _audioOutputDevice.EnsurePlaying();
            _playbackPositionTracker.ResetOutputEpoch();
        }

        private void FlushPhysicalOutputAndApplyPendingCommands()
        {
            _audioOutputDevice.FlushAndExecute(_audioMixer.ProcessPendingCommandsWithoutRendering);
            _playbackPositionTracker.ResetOutputEpoch();
        }

        private void ProcessCommandsWithoutOutputDevice()
        {
            _audioOutputDevice.ExecuteIfNotPlaying(_audioMixer.ProcessPendingCommandsWithoutRendering);
        }

        private void PollPlaybackCompletion(object _)
        {
            if (_isDisposed)
                return;
            if (Interlocked.Exchange(ref _isCompletionPollActive, 1) != 0)
                return;
            try
            {
                var absoluteOutputFrame = _playbackPositionTracker.CaptureAbsoluteOutputFrame();
                ReportCompletedVoices(absoluteOutputFrame);

                var completedPlaybackGeneration = _audioMixer.CompletedPlaybackGeneration;
                if (completedPlaybackGeneration == 0 || completedPlaybackGeneration == Interlocked.Read(ref _lastReportedCompletedGeneration))
                    return;
                if (_audioMixer.LastAppliedCommandSequence < _playbackPositionTracker.PendingPositionCommandSequence)
                    return;
                var completedOutputFrame = _audioMixer.CompletedOutputFrame;
                if (completedOutputFrame >= 0
                    && absoluteOutputFrame.HasValue
                    && absoluteOutputFrame.Value < completedOutputFrame)
                    return;

                Interlocked.Exchange(ref _lastReportedCompletedGeneration, completedPlaybackGeneration);
                Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Stopped);
                PlaybackCompleted?.Invoke(completedPlaybackGeneration);
            }
            finally
            {
                Volatile.Write(ref _isCompletionPollActive, 0);
                if (!_isDisposed && PlaybackState == SoundPlaybackState.Playing)
                    SchedulePlaybackCompletionPoll();
            }
        }

        private void ReportCompletedVoices(long? absoluteOutputFrame)
        {
            while (_audioMixer.TryPeekCompletedVoice(out var completion))
            {
                if (completion.VoiceHandle.PlaybackGeneration == PlaybackGeneration
                    && absoluteOutputFrame.HasValue
                    && absoluteOutputFrame.Value < completion.AbsoluteOutputFrame)
                    return;

                _audioMixer.TryDequeueCompletedVoice(out completion);
                if (completion.VoiceHandle.PlaybackGeneration == PlaybackGeneration)
                    VoiceCompleted?.Invoke(completion.VoiceHandle);
            }
        }

        private void SchedulePlaybackCompletionPoll()
        {
            if (_isDisposed)
                return;
            try
            {
                _playbackCompletionTimer.Change(PlaybackCompletionPollIntervalMilliseconds, Timeout.Infinite);
            }
            catch (ObjectDisposedException) when (_isDisposed)
            {
            }
        }

        // Checked here rather than in the callback: this runs on the control thread, so a
        // mismatch can be reported instead of silently dropping the voice mid-render.
        private static byte[] GetPcmData(SoundEngineCacheEntry audioEntry)
        {
            var audio = audioEntry.Audio;
            if (audio.SampleRate != PlaybackFormat.SampleRate
                || audio.Channels != PlaybackFormat.ChannelCount
                || audio.BitsPerSample != PlaybackFormat.BitsPerSample)
                throw new ArgumentException(
                    $"Audio must be {PlaybackFormat.SampleRate} Hz, {PlaybackFormat.ChannelCount} channel, "
                    + $"{PlaybackFormat.BitsPerSample}-bit float to be played, but was {audio.SampleRate} Hz, "
                    + $"{audio.Channels} channel, {audio.BitsPerSample}-bit.",
                    nameof(audioEntry));
            return audio.Data;
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            _playbackCompletionTimer.Dispose();
            _audioMixer.SilenceImmediately();
            _audioMixer.SubmitCommand(MixerCommand.Stop());
            FlushPhysicalOutputAndApplyPendingCommands();
            _audioOutputDevice.Dispose();
        }
    }
}
