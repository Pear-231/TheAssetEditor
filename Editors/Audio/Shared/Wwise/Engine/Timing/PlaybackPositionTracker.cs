using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Output;

namespace Editors.Audio.Shared.Wwise.Engine.Timing
{
    internal sealed class PlaybackPositionTracker(AudioMixer audioMixer, IAudioOutputDevice audioOutputDevice)
    {
        private readonly object _devicePositionLock = new();
        private long _pendingPlaybackPositionFrames;
        private long _pendingPositionCommandSequence;
        private long _outputEpochRenderedFrameCount;
        private long _outputEpochDevicePositionBytes;
        private long _devicePositionCarryBytes;
        private long _lastRawDevicePositionBytes;

        public long PendingPositionCommandSequence => Interlocked.Read(ref _pendingPositionCommandSequence);

        public void SetPendingPosition(long playbackPositionFrame, long commandSequence)
        {
            Interlocked.Exchange(ref _pendingPlaybackPositionFrames, playbackPositionFrame);
            Interlocked.Exchange(ref _pendingPositionCommandSequence, commandSequence);
        }

        public void ResetOutputEpoch()
        {
            Interlocked.Exchange(ref _outputEpochRenderedFrameCount, audioMixer.RenderedOutputFrameCount);
            Interlocked.Exchange(
                ref _outputEpochDevicePositionBytes,
                CaptureMonotonicOutputPosition()?.PositionBytes ?? 0);
        }

        public long? CaptureAbsoluteOutputFrame()
        {
            var outputEpochFrame = Interlocked.Read(ref _outputEpochRenderedFrameCount);
            var outputDevicePosition = CaptureMonotonicOutputPosition();
            if (!outputDevicePosition.HasValue)
                return null;

            var elapsedDevicePlaybackTime = TimeSpan.FromSeconds(
                (outputDevicePosition.Value.PositionBytes - Interlocked.Read(ref _outputEpochDevicePositionBytes))
                / (double)outputDevicePosition.Value.AverageBytesPerSecond);
            return outputEpochFrame + PlaybackTime.ToFrames(elapsedDevicePlaybackTime);
        }

        // The output client can reset its clock to zero after playback has already resumed, so carry
        // the pre-restart bytes forward: rewriting only the epoch's device half would leave the
        // rendered half anchored to the pause and drag every later position back to it.
        private AudioDevicePosition? CaptureMonotonicOutputPosition()
        {
            var outputDevicePosition = audioOutputDevice.CaptureOutputPosition();
            if (!outputDevicePosition.HasValue)
                return null;

            var rawDevicePositionBytes = outputDevicePosition.Value.PositionBytes;
            lock (_devicePositionLock)
            {
                if (rawDevicePositionBytes < _lastRawDevicePositionBytes)
                    _devicePositionCarryBytes += _lastRawDevicePositionBytes;
                _lastRawDevicePositionBytes = rawDevicePositionBytes;
                return new AudioDevicePosition(
                    _devicePositionCarryBytes + rawDevicePositionBytes,
                    outputDevicePosition.Value.AverageBytesPerSecond);
            }
        }

        public long GetMixerPlaybackPositionFrames()
        {
            if (audioMixer.LastAppliedCommandSequence < Interlocked.Read(ref _pendingPositionCommandSequence))
                return Interlocked.Read(ref _pendingPlaybackPositionFrames);
            else
                return audioMixer.PlaybackPositionFrames;
        }

        public long GetAudiblePositionFrames(
            SoundPlaybackState playbackState,
            long playbackGeneration,
            long? absoluteOutputFrame)
        {
            var mixerPlaybackPosition = GetMixerPlaybackPositionFrames();
            if (playbackState != SoundPlaybackState.Playing
                || !absoluteOutputFrame.HasValue)
                return mixerPlaybackPosition;

            return audioMixer.TryGetTimelineFrame(
                absoluteOutputFrame.Value,
                playbackGeneration,
                out var audiblePositionFrame)
                ? audiblePositionFrame
                : mixerPlaybackPosition;
        }
    }
}
