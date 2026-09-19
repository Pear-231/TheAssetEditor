using System.IO;
using System.Security.Cryptography;
using Shared.Core.PackFiles;
using Shared.GameFormats.Audio.Containers.Wav;
using Shared.GameFormats.Audio.Formats.Pcm;
using Shared.GameFormats.Wwise.Wem.V132;

namespace Editors.Audio.Shared.Wwise.Engine.Media
{
    // Bank media, kept decoded and resident — the Stream Manager's job in Wwise, and correctly
    // inside the engine.
    //
    // This is a control thread type. The audio thread must never call it: the lock below is the
    // hazard rather than a miss, because a lookup that lands while another animation is being
    // loaded would block the render callback. Voices are handed a SourceMedia reference and read
    // it directly, so a voice that outlives its cache entry simply keeps the samples alive.
    public sealed class MediaCache
    {
        private const long DefaultCapacityBytes = 256L * 1024 * 1024;

        private enum AudioEncoding
        {
            Wem = 1,
            Wave = 2
        }

        private readonly object _cacheLock = new();
        private readonly Dictionary<string, SourceMedia> _mediaByContentHash = new(StringComparer.Ordinal);
        private readonly IPackFileService _packFileService;
        private long _cachedBytes;
        private long _accessSequence;

        public MediaCache(IPackFileService packFileService, long capacityBytes = DefaultCapacityBytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);
            _packFileService = packFileService;
            CapacityBytes = capacityBytes;
        }

        public long CapacityBytes { get; }
        internal long CachedBytes { get { lock (_cacheLock) return _cachedBytes; } }
        internal int CachedMediaCount { get { lock (_cacheLock) return _mediaByContentHash.Count; } }

        public SourceMedia GetWem(ReadOnlySpan<byte> wemBytes)
            => Get(wemBytes, AudioEncoding.Wem);

        public SourceMedia GetWave(ReadOnlySpan<byte> waveBytes)
            => Get(waveBytes, AudioEncoding.Wave);

        public SourceMedia GetFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            var packFile = _packFileService.FindFile(filePath)
                ?? throw new FileNotFoundException("Audio file was not found in the pack file service.", filePath);
            return GetWave(packFile.DataSource.ReadData());
        }

        private SourceMedia Get(ReadOnlySpan<byte> encodedBytes, AudioEncoding audioEncoding)
        {
            if (encodedBytes.IsEmpty)
                throw new ArgumentNullException(nameof(encodedBytes));

            var contentHash = ComputeContentHash(encodedBytes, audioEncoding);
            lock (_cacheLock)
            {
                if (_mediaByContentHash.TryGetValue(contentHash, out var cachedMedia))
                {
                    cachedMedia.LastAccessSequence = ++_accessSequence;
                    return cachedMedia;
                }

                var loadedMedia = Load(contentHash, encodedBytes.ToArray(), audioEncoding);
                loadedMedia.LastAccessSequence = ++_accessSequence;
                _mediaByContentHash.Add(contentHash, loadedMedia);
                _cachedBytes += loadedMedia.Size;
                EvictLeastRecentlyUsed(loadedMedia);
                return loadedMedia;
            }
        }

        // Decoded to float and otherwise left exactly as authored. The channel collapse and the
        // rate conversion that used to happen here are the voice's work now.
        private static SourceMedia Load(string contentHash, byte[] encodedBytes, AudioEncoding audioEncoding)
        {
            PcmAudio decodedAudio;
            uint channelMask;
            int? loopStartFrame;
            int? loopEndFrame;
            if (audioEncoding == AudioEncoding.Wem)
            {
                var wemFile = WemFile.CreateFromWemBytes(encodedBytes);
                decodedAudio = PcmAudio.CreateFromWemBytes(encodedBytes);
                channelMask = DecodeWemChannelMask(wemFile.FmtChunk.ChannelMask);
                ReadLoop(wemFile.SmplChunk, out loopStartFrame, out loopEndFrame);
            }
            else
            {
                var waveFile = WavFile.CreateFromBytes(encodedBytes);
                decodedAudio = waveFile.Audio;
                channelMask = waveFile.FmtChunk.ChannelMask;
                ReadLoop(waveFile.SmplChunk, out loopStartFrame, out loopEndFrame);
            }

            if (decodedAudio.Data.Length == 0 || decodedAudio.Channels == 0 || decodedAudio.SampleRate == 0)
                throw new InvalidDataException("Audio has no decodable samples.");

            var samples = decodedAudio.ToInterleavedSamples();
            if (samples.Length / decodedAudio.Channels == 0)
                throw new InvalidDataException("Audio has no complete sample frames.");

            var frameCount = samples.Length / decodedAudio.Channels;
            if (loopEndFrame.HasValue && loopEndFrame.Value > frameCount)
                throw new InvalidDataException("The authored media loop extends beyond the decoded samples.");

            return SourceMedia.FromOwnedSamples(
                contentHash,
                samples,
                decodedAudio.Channels,
                (int)decodedAudio.SampleRate,
                channelMask,
                loopStartFrame,
                loopEndFrame);
        }

        private static uint DecodeWemChannelMask(uint packedChannelMask)
            => packedChannelMask >> 12;

        private static void ReadLoop(
            global::Shared.GameFormats.Wwise.Wem.V132.SmplChunk smplChunk,
            out int? loopStartFrame,
            out int? loopEndFrame)
        {
            loopStartFrame = smplChunk?.HasForwardLoop == true ? checked((int)smplChunk.LoopStartFrame) : null;
            loopEndFrame = smplChunk?.HasForwardLoop == true ? checked((int)smplChunk.LoopEndFrame) : null;
        }

        private static void ReadLoop(
            global::Shared.GameFormats.Audio.Containers.Wav.SmplChunk smplChunk,
            out int? loopStartFrame,
            out int? loopEndFrame)
        {
            loopStartFrame = smplChunk?.HasForwardLoop == true ? checked((int)smplChunk.LoopStartFrame) : null;
            loopEndFrame = smplChunk?.HasForwardLoop == true ? checked((int)smplChunk.LoopEndFrame) : null;
        }

        private static string ComputeContentHash(ReadOnlySpan<byte> encodedBytes, AudioEncoding audioEncoding)
        {
            using var contentHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> audioEncodingBytes = [(byte)audioEncoding];
            contentHasher.AppendData(audioEncodingBytes);
            contentHasher.AppendData(encodedBytes);
            Span<byte> contentHashBytes = stackalloc byte[32];
            if (!contentHasher.TryGetHashAndReset(contentHashBytes, out var bytesWritten)
                || bytesWritten != contentHashBytes.Length)
                throw new CryptographicException("Could not calculate the cached audio content hash.");
            return Convert.ToHexString(contentHashBytes);
        }

        private void EvictLeastRecentlyUsed(SourceMedia retainedMedia)
        {
            while (_cachedBytes > CapacityBytes && _mediaByContentHash.Count > 1)
            {
                var evictionCandidate = _mediaByContentHash.Values
                    .Where(media => !ReferenceEquals(media, retainedMedia))
                    .MinBy(media => media.LastAccessSequence);
                if (evictionCandidate == null)
                    return;

                _mediaByContentHash.Remove(evictionCandidate.ContentHash);
                _cachedBytes -= evictionCandidate.Size;
            }
        }
    }
}
