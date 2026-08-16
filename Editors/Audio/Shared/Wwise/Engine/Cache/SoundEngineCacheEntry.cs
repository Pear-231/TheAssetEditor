using Shared.GameFormats.Audio.Formats.Pcm;

namespace Editors.Audio.Shared.Wwise.Engine.Cache
{
    public sealed class SoundEngineCacheEntry(string contentHash, PcmAudio audio)
    {
        public string ContentHash { get; } = contentHash;
        public PcmAudio Audio { get; } = audio;
        internal long Size => Audio.Data.Length;
        internal long LastAccessSequence { get; set; }
    }
}
