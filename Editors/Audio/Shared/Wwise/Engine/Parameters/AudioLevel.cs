namespace Editors.Audio.Shared.Wwise.Engine.Parameters
{
    // Decibels to the gain a sample is multiplied by, in the one place that does it.
    //
    // Wwise authors volume in decibels and mixes in linear gain, so the conversion has to happen
    // exactly once on the way from the bank to the voice. Doing it per sample would be both wasteful
    // and wrong, because the sum of the hierarchy's decibels is what converts, not each level of it.
    internal static class AudioLevel
    {
        // Wwise's own floor. Anything at or below it is silence rather than a very small number, so
        // a muted branch costs nothing to mix and cannot leak a denormal into the bus.
        public const float SilenceDecibels = -96f;

        // The top of the range Wwise authors: +12 dB of gain, four times the amplitude.
        public const float MaximumDecibels = 12f;

        public static float DecibelsToLinear(float decibels)
        {
            if (decibels <= SilenceDecibels)
                return 0f;

            return MathF.Pow(10f, Math.Min(decibels, MaximumDecibels) / 20f);
        }

        public static float LinearToDecibels(float linear)
            => linear <= 0f ? SilenceDecibels : 20f * MathF.Log10(linear);
    }
}
