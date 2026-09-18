using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Wwise.Engine.Parameters
{
    internal static class RtpcCurve
    {
        public static float Evaluate(IReadOnlyList<ICAkLayerCntr.IAkRtpcGraphPoint> points, float input)
        {
            if (points.Count == 0)
                return float.NegativeInfinity;
            if (input <= points[0].From)
                return points[0].To;
            if (input >= points[^1].From)
                return points[^1].To;

            for (var pointIndex = 0; pointIndex < points.Count - 1; pointIndex++)
            {
                var first = points[pointIndex];
                var second = points[pointIndex + 1];
                if (input > second.From)
                    continue;

                var width = second.From - first.From;
                var position = width <= 0f ? 1f : Math.Clamp((input - first.From) / width, 0f, 1f);
                var shapedPosition = Shape(position, first.Interp);
                return first.To + (second.To - first.To) * shapedPosition;
            }

            return points[^1].To;
        }

        // Interpolates between the points and only then applies the curve's scaling, which is the
        // order Wwise uses: CAkConversionTable::ConvertInternal converts, then scales.
        public static float Evaluate(WwiseCurve curve, float input)
        {
            var points = curve.Points;
            if (curve.Count == 0)
                return 0f;
            if (input <= points[0].From)
                return curve.Scale(points[0].To);
            if (input >= points[^1].From)
                return curve.Scale(points[^1].To);
            for (var pointIndex = 0; pointIndex < points.Count - 1; pointIndex++)
            {
                var first = points[pointIndex];
                var second = points[pointIndex + 1];
                if (input > second.From)
                    continue;
                var width = second.From - first.From;
                var position = width <= 0f ? 1f : Math.Clamp((input - first.From) / width, 0f, 1f);
                return curve.Scale(first.To + (second.To - first.To) * Shape(position, first.Interpolation));
            }
            return curve.Scale(points[^1].To);
        }

        // AkCurveInterpolation's on-disk values. Sine and reciprocal-sine are the two halves of a
        // constant-power crossfade; Log/Exp 1 and 3 are progressively gentler/steeper variants.
        private static float Shape(float position, uint interpolation) => interpolation switch
        {
            0 => MathF.Pow(position, 1f / 3f),
            1 => MathF.Sin(position * MathF.PI / 2f),
            2 => MathF.Sqrt(position),
            3 => 1f - SmoothStep(1f - position),
            4 => position,
            5 => SmoothStep(position),
            6 => position * position,
            7 => 1f - MathF.Cos(position * MathF.PI / 2f),
            8 => position * position * position,
            9 => 0f,
            _ => position
        };

        private static float SmoothStep(float position)
            => position * position * (3f - 2f * position);
    }
}
