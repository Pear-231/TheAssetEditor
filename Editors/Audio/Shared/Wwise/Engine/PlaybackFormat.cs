using NAudio.Wave;

namespace Editors.Audio.Shared.Wwise.Engine
{
    // The format the mix and the busses run at, and what the sink is opened with.
    //
    // Not what audio has to be to be played: sources keep the rate and channel count they were
    // authored in, and the per voice resampler and panner bring them here.
    internal static class PlaybackFormat
    {
        public const int SampleRate = 48_000;
        public const int ChannelCount = 2;
        public static readonly WaveFormat WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, ChannelCount);
    }
}
