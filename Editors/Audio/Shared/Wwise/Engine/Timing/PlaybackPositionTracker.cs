using Editors.Audio.Shared.Wwise.Engine.Rendering;
using Editors.Audio.Shared.Wwise.Engine.Output;

namespace Editors.Audio.Shared.Wwise.Engine.Timing
{
    internal sealed class PlaybackPositionTracker(AudioRenderer audioRenderer, IAudioOutputDevice audioOutputDevice)
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
            Interlocked.Exchange(ref _outputEpochRenderedFrameCount, audioRenderer.RenderedOutputFrameCount);
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

        // A device position can go backwards for two entirely different reasons, and telling them
        // apart matters more than it sounds.
        //
        // The output client can reset its clock to zero after playback has already resumed, so the
        // pre-restart bytes are carried forward: rewriting only the epoch's device half would leave
        // the rendered half anchored to the pause and drag every later position back to it.
        //
        // But a WASAPI endpoint also reports its position with a little backwards jitter -- steps of
        // -3 and -46 bytes measured on a real endpoint -- and treating one of those as a restart
        // would add the whole elapsed position to the carry and roughly double the playhead.
        //
        // What separates them is where the position landed rather than how far it fell: a restart
        // lands nearer to zero than to where it was, and jitter stays next to where it was. That
        // holds at any scale, which an absolute threshold does not -- a restart from a low position
        // is a small step backwards and is still a restart.
        private AudioDevicePosition? CaptureMonotonicOutputPosition()
        {
            var outputDevicePosition = audioOutputDevice.CaptureOutputPosition();
            if (!outputDevicePosition.HasValue)
                return null;

            var rawDevicePositionBytes = outputDevicePosition.Value.PositionBytes;
            lock (_devicePositionLock)
            {
                if (rawDevicePositionBytes < _lastRawDevicePositionBytes)
                {
                    var backwardsBytes = _lastRawDevicePositionBytes - rawDevicePositionBytes;
                    if (rawDevicePositionBytes < backwardsBytes)
                        _devicePositionCarryBytes += _lastRawDevicePositionBytes;
                    else
                        rawDevicePositionBytes = _lastRawDevicePositionBytes;
                }

                _lastRawDevicePositionBytes = rawDevicePositionBytes;
                return new AudioDevicePosition(
                    _devicePositionCarryBytes + rawDevicePositionBytes,
                    outputDevicePosition.Value.AverageBytesPerSecond);
            }
        }

        public long GetRendererPlaybackPositionFrames()
        {
            if (audioRenderer.LastAppliedCommandSequence < Interlocked.Read(ref _pendingPositionCommandSequence))
                return Interlocked.Read(ref _pendingPlaybackPositionFrames);
            else
                return audioRenderer.PlaybackPositionFrames;
        }

        // The ledger is asked whatever the transport is doing, not only while it is playing. A pause
        // no longer stops the output device, so the frames already rendered ahead of the pause go
        // on being heard, and the playhead has to keep tracking them until the transport's ramp
        // freezes it. The ledger publishes that frozen frame from then on, so the position settles
        // on it by itself rather than jumping to wherever the renderer had run ahead to.
        public long GetAudiblePositionFrames(
            SoundPlaybackState playbackState,
            TransportId transportId,
            long? absoluteOutputFrame)
        {
            var rendererPlaybackPosition = GetRendererPlaybackPositionFrames();
            if (!absoluteOutputFrame.HasValue)
                return rendererPlaybackPosition;

            return audioRenderer.TryGetTimelineFrame(
                absoluteOutputFrame.Value,
                transportId,
                out var audiblePositionFrame)
                ? audiblePositionFrame
                : rendererPlaybackPosition;
        }
    }
}
