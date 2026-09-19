using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Hirc;
using Editors.Audio.Shared.Wwise.Engine.Parameters;

namespace Editors.Audio.Shared.Wwise.Engine.Behaviour
{
    internal sealed class AttenuationSettings
    {
        public static readonly AttenuationSettings None =
            new(WwiseCurve.Empty, WwiseCurve.Empty, WwiseCurve.Empty);

        private readonly WwiseCurve _volume;
        private readonly WwiseCurve _lowPassFilter;
        private readonly WwiseCurve _highPassFilter;

        public AttenuationSettings(
            WwiseCurve volume,
            WwiseCurve lowPassFilter,
            WwiseCurve highPassFilter)
        {
            _volume = volume;
            _lowPassFilter = lowPassFilter;
            _highPassFilter = highPassFilter;
        }

        public float VolumeDecibels(float distance) => RtpcCurve.Evaluate(_volume, distance);
        public float LowPassFilter(float distance) => RtpcCurve.Evaluate(_lowPassFilter, distance);
        public float HighPassFilter(float distance) => RtpcCurve.Evaluate(_highPassFilter, distance);
    }
}
