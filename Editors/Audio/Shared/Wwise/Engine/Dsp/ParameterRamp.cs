namespace Editors.Audio.Shared.Wwise.Engine.Dsp
{
    // Moves an authored property to a new value across frames rather than stepping it.
    //
    // Gain has GainRamp and is interpolated every frame, because a step in gain is a click. Pitch
    // and filter cutoff are not clicks: each is applied by recomputing a coefficient, which is too
    // expensive to do per sample and produces no discontinuity in the signal when it moves between
    // blocks. So this advances once per block — the same granularity the distance-driven filter
    // cutoff already moves at — and the ramp exists to stop a switch or state change jumping the
    // pitch and tone of a voice that is already sounding.
    internal sealed class ParameterRamp
    {
        private float _value;
        private float _targetValue;
        private float _valueStep;

        // Counted rather than compared against the target, for the same reason GainRamp counts:
        // accumulating a step does not land exactly on the target.
        private int _remainingRampFrames;

        public float Value => _value;
        public bool IsRamping => _remainingRampFrames > 0;

        public void SetImmediately(float value)
        {
            _value = value;
            _targetValue = value;
            _valueStep = 0f;
            _remainingRampFrames = 0;
        }

        public void RampTo(float targetValue, int rampFrames)
        {
            if (rampFrames <= 0 || targetValue == _value)
            {
                SetImmediately(targetValue);
                return;
            }

            _targetValue = targetValue;
            _remainingRampFrames = rampFrames;
            _valueStep = (targetValue - _value) / rampFrames;
        }

        // Advances a whole block at once and returns the value it should be rendered at.
        public float Advance(int frameCount)
        {
            if (_remainingRampFrames == 0)
                return _value;

            var advancingFrameCount = Math.Min(frameCount, _remainingRampFrames);
            _remainingRampFrames -= advancingFrameCount;
            _value = _remainingRampFrames == 0
                ? _targetValue
                : _value + _valueStep * advancingFrameCount;
            return _value;
        }
    }
}
