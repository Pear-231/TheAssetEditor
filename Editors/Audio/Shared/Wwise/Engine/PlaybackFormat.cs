using NAudio.Wave;

namespace Editors.Audio.Shared.Wwise.Engine
{
    // The rate the mixer runs at. Audio is decoded to this so voices, the timeline
    // and the output device never have to convert while rendering.
    internal static class PlaybackFormat
    {
        public const int SampleRate = 48_000;
        public const int ChannelCount = 2;
        public const int BitsPerSample = 32;
        public static readonly WaveFormat WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, ChannelCount);
    }
}
