namespace Editors.Audio.Shared.Wwise.Engine.Dsp
{
    // The low-pass and high-pass a voice authors, as one-pole filters per mix channel.
    //
    // Wwise authors both as a percentage rather than a frequency, and the curve it maps that
    // percentage onto is not published anywhere this project can read. What is implemented here is
    // the shape the percentage plainly has — 0 does nothing, 100 is as filtered as it goes, and the
    // cutoff moves exponentially between the two — which is honest about being an approximation of
    // Wwise's curve rather than a reproduction of it.
    //
    // One pole rather than a biquad because the authored control is a single number: a steeper
    // filter would need a resonance the bank never states.
    internal sealed class VoiceFilter
    {
        // Where the two filters end up at 100%. A low-pass that reaches 20 Hz is silence for
        // anything but a rumble, and a high-pass that reaches 20 kHz is silence for everything.
        private const float LowestCutoffHertz = 20f;
        private const float HighestCutoffHertz = 20_000f;

        private readonly float[] _lowPassState = new float[PlaybackFormat.ChannelCount];
        private readonly float[] _highPassState = new float[PlaybackFormat.ChannelCount];
        private readonly float[] _previousInput = new float[PlaybackFormat.ChannelCount];

        private float _lowPassCoefficient;
        private float _highPassCoefficient;
        private bool _hasLowPass;
        private bool _hasHighPass;

        public bool IsActive => _hasLowPass || _hasHighPass;

        public void Set(float lowPassPercent, float highPassPercent)
        {
            _hasLowPass = lowPassPercent > 0f;
            _hasHighPass = highPassPercent > 0f;

            // A low-pass at 100% has the cutoff at the bottom of the range; at 0% it is above
            // anything the mix can carry, which is what makes it inaudible rather than merely quiet.
            _lowPassCoefficient = _hasLowPass
                ? LowPassCoefficient(CutoffFor(HighestCutoffHertz, LowestCutoffHertz, lowPassPercent))
                : 0f;
            _highPassCoefficient = _hasHighPass
                ? HighPassCoefficient(CutoffFor(LowestCutoffHertz, HighestCutoffHertz, highPassPercent))
                : 0f;
        }

        public void Reset()
        {
            Array.Clear(_lowPassState);
            Array.Clear(_highPassState);
            Array.Clear(_previousInput);
        }

        public float Process(int channel, float sample)
        {
            if (_hasLowPass)
            {
                _lowPassState[channel] += _lowPassCoefficient * (sample - _lowPassState[channel]);
                sample = _lowPassState[channel];
            }

            if (_hasHighPass)
            {
                // The difference form: what the low-pass would have removed is what a high-pass
                // keeps, without a second state variable diverging from the first.
                _highPassState[channel] = _highPassCoefficient * (_highPassState[channel] + sample - _previousInput[channel]);
                _previousInput[channel] = sample;
                sample = _highPassState[channel];
            }

            return sample;
        }

        // Exponential between the two ends, because a filter sweep that moves linearly in hertz
        // sounds like it stops moving as soon as it leaves the top of the range.
        private static float CutoffFor(float startHertz, float endHertz, float percent)
        {
            var amount = Math.Clamp(percent, 0f, 100f) / 100f;
            return startHertz * MathF.Pow(endHertz / startHertz, amount);
        }

        // How much of the way towards the input one frame moves the filter's state.
        private static float LowPassCoefficient(float cutoffHertz)
            => Math.Clamp(1f - MathF.Exp(-Radians(cutoffHertz)), 0f, 1f);

        // How much of its own output the high-pass keeps, which is the complement of the same pole.
        private static float HighPassCoefficient(float cutoffHertz)
            => Math.Clamp(MathF.Exp(-Radians(cutoffHertz)), 0f, 1f);

        private static float Radians(float cutoffHertz)
            => 2f * MathF.PI * Math.Clamp(cutoffHertz / PlaybackFormat.SampleRate, 0.0001f, 0.49f);
    }
}
