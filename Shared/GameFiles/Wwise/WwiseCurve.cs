namespace Shared.GameFormats.Wwise
{
    // How a stored curve's values are encoded. Wwise interpolates between the points first and
    // scales the result afterwards, so this cannot be folded into the points when they are read.
    public enum AkCurveScaling : byte
    {
        None = 0,
        Decibels = 2,
        Logarithmic = 3,
        DecibelsToLinear = 4
    }

    public readonly record struct WwiseCurvePoint(float From, float To, uint Interpolation);

    // A curve and the scaling its values are stored in. The two travel together because a point
    // read without its scaling is not a value: a volume curve reaching -1 means silence, not one
    // decibel down.
    public readonly record struct WwiseCurve(AkCurveScaling Scaling, IReadOnlyList<WwiseCurvePoint> Points)
    {
        // The quietest a decibel-scaled curve can reach, which is what a stored -1 means.
        public const float MinimumDecibels = -96.3f;

        public static readonly WwiseCurve Empty = new(AkCurveScaling.None, []);

        public int Count => Points?.Count ?? 0;

        // CAkConversionTable::ApplyCurveScaling, for bank versions 72 and later.
        public float Scale(float value) => Scaling switch
        {
            AkCurveScaling.Decibels => ScaleDecibels(value),
            AkCurveScaling.Logarithmic => MathF.Pow(10f, value / 20f),
            AkCurveScaling.DecibelsToLinear => MathF.Pow(10f, value * 0.05f),
            _ => value
        };

        private static float ScaleDecibels(float value)
        {
            var clamped = Math.Clamp(value, -1f, 1f);
            return clamped == -1f ? MinimumDecibels : MathF.Log10(clamped + 1f) * 20f;
        }
    }
}
