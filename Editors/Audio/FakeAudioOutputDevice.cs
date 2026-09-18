using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Output;
using NAudio.Wave;

namespace Test.Audio
{
    // Pulls buffers on demand instead of on a device callback, so a test decides exactly how many
    // frames have been rendered and how many of them the device has drained.
    internal sealed class FakeAudioOutputDevice : IAudioOutputDevice
    {
        private ISampleProvider? _audioSource;

        public int CreationCount { get; private set; }
        public int EnsurePlayingCount { get; private set; }
        public long DevicePositionFrames { get; set; }
        public bool IsPlaying { get; private set; }

        public bool EnsureCreated(ISampleProvider audioSource)
        {
            if (_audioSource != null)
                return false;

            _audioSource = audioSource;
            CreationCount++;
            return true;
        }

        // Set to have the next EnsurePlaying behave as a device that has been taken away and
        // rebuilt: the clock starts again from zero, which is what the position epoch exists for.
        public bool HasLostItsEndpoint { get; set; }

        public int RebuildCount { get; private set; }

        public bool EnsurePlaying()
        {
            EnsurePlayingCount++;
            if (HasLostItsEndpoint)
            {
                HasLostItsEndpoint = false;
                RebuildCount++;
                IsPlaying = true;
                DevicePositionFrames = 0;
                return true;
            }

            var wasStopped = !IsPlaying;
            IsPlaying = true;
            return wasStopped;
        }

        public AudioDevicePosition? CaptureOutputPosition()
            => _audioSource == null
                ? null
                : new AudioDevicePosition(
                    DevicePositionFrames * PlaybackFormat.ChannelCount * sizeof(float),
                    PlaybackFormat.WaveFormat.AverageBytesPerSecond);

        // Returns what was rendered, so a test can assert on the audio itself and not only on the
        // state the engine ended up in.
        public float[] Render(int frameCount)
        {
            if (_audioSource == null)
                throw new InvalidOperationException("The output device has not been created.");
            var buffer = new float[frameCount * PlaybackFormat.ChannelCount];
            _audioSource.Read(buffer, 0, buffer.Length);
            return buffer;
        }

        public void Dispose()
        {
            IsPlaying = false;
            _audioSource = null;
        }
    }
}
