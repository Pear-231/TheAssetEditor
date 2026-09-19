namespace Editors.Audio.Shared.Wwise.Engine.Timing
{
    internal static class PlaybackTime
    {
        public static long ToFrames(TimeSpan playbackTime)
            => Math.Max(0, (long)Math.Round(playbackTime.TotalSeconds * PlaybackFormat.SampleRate));

        public static TimeSpan FromFrames(long frameCount)
            => TimeSpan.FromSeconds(frameCount / (double)PlaybackFormat.SampleRate);
    }
}
