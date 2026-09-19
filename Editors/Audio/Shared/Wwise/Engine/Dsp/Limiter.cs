namespace Editors.Audio.Shared.Wwise.Engine.Dsp
{
    // Overload handled once, on the master bus, instead of by clamping every sample in the mix
    // loop. Hard clipping is a failure mode rather than a strategy: three overlapping full scale
    // cues used to put roughly half the output samples flat against the rail.
    //
    // Deliberately without look-ahead. Look-ahead buys a smoother reduction at the cost of a fixed
    // output delay, and the ledger maps output frames to timeline frames as they are rendered, so
    // that delay would leave the audio behind the position the animation is drawn from. It can only
    // be revisited alongside the ledger.
    internal sealed class Limiter
    {
        // -1 dBFS, so the output sits visibly short of the rail rather than on it.
        private const float CeilingAmplitude = 0.891251f;
        private const float ReleaseSeconds = 0.05f;

        private readonly float _releaseCoefficient =
            1f - MathF.Exp(-1f / (ReleaseSeconds * PlaybackFormat.SampleRate));
        private float _gain = 1f;

        // Returns the highest level it saw before reducing anything, which is what the mix asked
        // for rather than what was allowed out. The limiter is already looking at every frame, so
        // reporting it here costs a comparison rather than another pass over the block.
        public float Process(float[] samples, int frameCount)
        {
            var blockPeakAmplitude = 0f;
            for (var frame = 0; frame < frameCount; frame++)
            {
                var firstSampleIndex = frame * PlaybackFormat.ChannelCount;
                var peakAmplitude = 0f;
                for (var channel = 0; channel < PlaybackFormat.ChannelCount; channel++)
                    peakAmplitude = MathF.Max(peakAmplitude, MathF.Abs(samples[firstSampleIndex + channel]));
                blockPeakAmplitude = MathF.Max(blockPeakAmplitude, peakAmplitude);

                // The gain is taken from the peak of the frame it is about to be applied to, so a
                // peak can never get through. Coming back up is slow, so the reduction is heard as
                // a level rather than as distortion.
                var requiredGain = peakAmplitude > CeilingAmplitude ? CeilingAmplitude / peakAmplitude : 1f;
                _gain = requiredGain < _gain
                    ? requiredGain
                    : _gain + (requiredGain - _gain) * _releaseCoefficient;

                for (var channel = 0; channel < PlaybackFormat.ChannelCount; channel++)
                    samples[firstSampleIndex + channel] *= _gain;
            }
            return blockPeakAmplitude;
        }
    }
}
