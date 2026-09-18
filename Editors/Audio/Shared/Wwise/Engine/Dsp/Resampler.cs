using Editors.Audio.Shared.Wwise.Engine.Media;

namespace Editors.Audio.Shared.Wwise.Engine.Dsp
{
    // A band-limited windowed-sinc rate converter. The kernel is centred on the requested source
    // position and reads decoded resident media in both directions, so it has no causal group delay
    // to hide from the playhead or output ledger. Kernel work is fixed and allocation free.
    internal sealed class Resampler
    {
        internal const int TapCount = 32;
        internal const int GroupDelayFrames = 0;
        internal const int PhaseCount = 256;
        private const double PassbandMargin = 0.94d;

        private readonly float[] _kernel = new float[TapCount * PhaseCount];
        private double _sourceFramesPerRateConversion = 1d;
        private double _sourceFramesPerOutputFrame = 1d;
        private double _sourceFramePosition;
        private double _kernelStep = double.NaN;

        public double SourceFramePosition => _sourceFramePosition;

        public void Start(SourceMedia media, double sourceFramePosition)
        {
            _sourceFramesPerRateConversion = media.SampleRate / (double)PlaybackFormat.SampleRate;
            _sourceFramesPerOutputFrame = _sourceFramesPerRateConversion;
            _sourceFramePosition = sourceFramePosition;
            RebuildKernel();
        }

        public void SetPitchCents(float pitchCents)
        {
            var pitchRatio = pitchCents == 0f ? 1d : Math.Pow(2d, pitchCents / 1200d);
            _sourceFramesPerOutputFrame = _sourceFramesPerRateConversion * pitchRatio;
            if (_sourceFramesPerOutputFrame != _kernelStep)
                RebuildKernel();
        }

        public void SeekTo(double sourceFramePosition) => _sourceFramePosition = sourceFramePosition;

        public void Advance() => _sourceFramePosition += _sourceFramesPerOutputFrame;

        public int OutputFramesUntil(double sourceFramePosition)
            => (int)Math.Max(0, Math.Ceiling((sourceFramePosition - _sourceFramePosition) / _sourceFramesPerOutputFrame));

        public double ToSourceFrames(long mixFrames) => mixFrames * _sourceFramesPerRateConversion;

        public float ReadChannel(SourceMedia media, int channel, int loopStartFrame = 0, int loopEndFrame = 0)
        {
            if (_sourceFramesPerOutputFrame == 1d && _sourceFramePosition == Math.Truncate(_sourceFramePosition))
                return ReadFrame(media, (int)_sourceFramePosition, channel, loopStartFrame, loopEndFrame);

            var centreFrame = (int)Math.Floor(_sourceFramePosition);
            var firstFrame = centreFrame - TapCount / 2 + 1;
            var weightedSample = 0d;
            var fraction = _sourceFramePosition - centreFrame;
            var phase = Math.Min(PhaseCount - 1, (int)(fraction * PhaseCount));
            var firstKernelIndex = phase * TapCount;

            for (var tap = 0; tap < TapCount; tap++)
                weightedSample += ReadFrame(media, firstFrame + tap, channel, loopStartFrame, loopEndFrame)
                    * _kernel[firstKernelIndex + tap];

            return (float)weightedSample;
        }

        private void RebuildKernel()
        {
            var cutOff = Math.Min(1d, 1d / _sourceFramesPerOutputFrame) * PassbandMargin;
            for (var phase = 0; phase < PhaseCount; phase++)
            {
                var fraction = phase / (double)PhaseCount;
                var weightSum = 0d;
                var firstKernelIndex = phase * TapCount;
                for (var tap = 0; tap < TapCount; tap++)
                {
                    var distance = tap - TapCount / 2 + 1 - fraction;
                    var windowPosition = (tap + 0.5d) / TapCount;
                    var window = 0.42d
                        - 0.5d * Math.Cos(2d * Math.PI * windowPosition)
                        + 0.08d * Math.Cos(4d * Math.PI * windowPosition);
                    var scaledDistance = Math.PI * distance * cutOff;
                    var sinc = Math.Abs(scaledDistance) < 1e-12
                        ? cutOff
                        : Math.Sin(scaledDistance) / (Math.PI * distance);
                    var weight = sinc * window;
                    _kernel[firstKernelIndex + tap] = (float)weight;
                    weightSum += weight;
                }
                if (weightSum == 0d)
                    continue;
                for (var tap = 0; tap < TapCount; tap++)
                    _kernel[firstKernelIndex + tap] = (float)(_kernel[firstKernelIndex + tap] / weightSum);
            }
            _kernelStep = _sourceFramesPerOutputFrame;
        }

        private static float ReadFrame(SourceMedia media, int frame, int channel, int loopStartFrame, int loopEndFrame)
        {
            if (loopEndFrame > loopStartFrame)
            {
                var loopLength = loopEndFrame - loopStartFrame;
                if (frame >= loopEndFrame)
                    frame = loopStartFrame + (frame - loopStartFrame) % loopLength;
                else if (frame < 0)
                    frame = loopEndFrame - 1 - (-frame - 1) % loopLength;
            }

            frame = Math.Clamp(frame, 0, media.FrameCount - 1);
            return media.Samples[frame * media.ChannelCount + channel];
        }
    }
}
