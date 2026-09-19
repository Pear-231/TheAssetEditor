namespace Editors.Audio.Shared.Wwise.Engine.Dsp
{
    internal enum GainRampCurve
    {
        Linear,
        EqualPower
    }

    // Interpolates a gain across frames instead of stepping it.
    //
    // A de-click ramp is not an authored fade. It is short enough to be inaudible as a level
    // change, and it is applied *only* where the transport itself created a discontinuity — a
    // pause, a resume, a voice starting part way through its media. Never on a sound's own start or
    // end, where it would soften an authored attack transient.
    internal sealed class GainRamp
    {
        // 3 ms. Long enough to remove the step, short enough that nothing sounds like it faded.
        public const int DeclickFrames = 144;

        // The same ramp, for the one caller that has to wait out wall-clock time rather than count
        // frames: the output device, deciding how long to let a ramp reach the speakers.
        public const int DeclickMilliseconds = 3;

        // 20 ms, and deliberately not the de-click ramp.
        //
        // A seek joins two arbitrary points of the same media, so the cross-fade has to drag the
        // phase from one to the other. Over 3 ms a large phase difference becomes a large
        // momentary frequency deviation -- measured at 630 Hz on a 440 Hz tone -- which is heard as
        // a bump on tonal material. Spreading the same phase difference over 20 ms reduces the
        // deviation by about the ratio of the two.
        //
        // Twenty milliseconds because this is an editor: seeking is what the waveform visualiser's
        // playhead and Super View's timeline do constantly, on dialogue, music and foley, which is
        // exactly the tonal material that shows the artefact. Still far below what reads as a fade.
        public const int SeekCrossFadeFrames = 960;

        private float _gain = 1f;
        private float _targetGain = 1f;
        private float _gainStep;
        private float _startGain;
        private int _totalRampFrames;
        private GainRampCurve _curve;

        // Counted rather than derived from comparing the gain against its target: accumulating a
        // step 144 times does not land exactly on the target, and a ramp that overran by a frame
        // would leave the transport freezing a frame late.
        private int _remainingRampFrames;

        public float Gain => _gain;
        public bool IsRamping => _remainingRampFrames > 0;
        public bool IsSilent => _remainingRampFrames == 0 && _gain == 0f;
        public bool IsUnityGain => _remainingRampFrames == 0 && _gain == 1f;

        // Lets the renderer end a block exactly where the ramp does, the same way it ends one
        // exactly where a cue starts, so the transport freezes on the frame it reached silence on
        // rather than wherever the block happened to end.
        public int RemainingFrames => _remainingRampFrames;

        public void SetImmediately(float gain)
        {
            _gain = gain;
            _targetGain = gain;
            _gainStep = 0f;
            _remainingRampFrames = 0;
            _totalRampFrames = 0;
        }

        public void DeclickIn() => RampTo(1f, DeclickFrames);

        public void DeclickOut() => RampTo(0f, DeclickFrames);

        public void RampTo(float targetGain, int rampFrames, GainRampCurve curve = GainRampCurve.Linear)
        {
            if (rampFrames <= 0 || targetGain == _gain)
            {
                SetImmediately(targetGain);
                return;
            }

            _targetGain = targetGain;
            _startGain = _gain;
            _totalRampFrames = rampFrames;
            _curve = curve;
            _remainingRampFrames = rampFrames;
            _gainStep = (targetGain - _gain) / rampFrames;
        }

        // Advances one frame and returns the gain that frame should be rendered at.
        public float NextGain()
        {
            if (_remainingRampFrames == 0)
                return _gain;

            _remainingRampFrames--;
            if (_remainingRampFrames == 0)
                _gain = _targetGain;
            else if (_curve == GainRampCurve.EqualPower && _startGain is 0f or 1f && _targetGain is 0f or 1f)
            {
                var progress = 1f - _remainingRampFrames / (float)_totalRampFrames;
                _gain = _targetGain > _startGain
                    ? MathF.Sin(progress * MathF.PI / 2f)
                    : MathF.Cos(progress * MathF.PI / 2f);
            }
            else
                _gain += _gainStep;
            return _gain;
        }
    }
}
