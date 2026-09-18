using Editors.Audio.Shared.Wwise.Engine.Dsp;

namespace Editors.Audio.Shared.Wwise.Engine.Rendering
{
    // A summing point for a block of audio. With a single master bus it does little more than hold
    // the block, but having the stage at all is what gives the limiter, effect slots and aux sends
    // somewhere to live rather than being folded into the mix loop.
    internal sealed class Bus
    {
        private readonly float[] _samples;

        public Bus(int maximumBlockFrames)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBlockFrames);
            _samples = new float[maximumBlockFrames * PlaybackFormat.ChannelCount];
        }

        // Voices accumulate straight into this, and the bus stages read and write it in place, so
        // nothing between a voice and the sink copies a block.
        public float[] Samples => _samples;

        public void Clear(int frameCount)
            => Array.Clear(_samples, 0, frameCount * PlaybackFormat.ChannelCount);

        public void ApplyGain(GainRamp gainRamp, int frameCount)
        {
            if (gainRamp.IsUnityGain)
                return;

            for (var frame = 0; frame < frameCount; frame++)
            {
                var gain = gainRamp.NextGain();
                var firstSampleIndex = frame * PlaybackFormat.ChannelCount;
                for (var channel = 0; channel < PlaybackFormat.ChannelCount; channel++)
                    _samples[firstSampleIndex + channel] *= gain;
            }
        }

        public void MixInto(float[] destinationBuffer, int destinationSampleOffset, int frameCount)
        {
            var sampleCount = frameCount * PlaybackFormat.ChannelCount;
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                destinationBuffer[destinationSampleOffset + sampleIndex] += _samples[sampleIndex];
        }

        public void AddFrom(Bus source, int frameCount)
        {
            var sampleCount = frameCount * PlaybackFormat.ChannelCount;
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                _samples[sampleIndex] += source._samples[sampleIndex];
        }

        public float MeasurePeak(int frameCount)
        {
            var peak = 0f;
            var sampleCount = frameCount * PlaybackFormat.ChannelCount;
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                peak = Math.Max(peak, Math.Abs(_samples[sampleIndex]));
            return peak;
        }
    }
}
