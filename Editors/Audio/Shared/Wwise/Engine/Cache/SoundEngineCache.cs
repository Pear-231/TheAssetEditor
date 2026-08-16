using System.IO;
using System.Security.Cryptography;
using Shared.Core.PackFiles;
using Shared.GameFormats.Audio.Containers.Wav;
using Shared.GameFormats.Audio.Formats.Pcm;

namespace Editors.Audio.Shared.Wwise.Engine.Cache
{
    public sealed class SoundEngineCache
    {
        private const long DefaultCapacityBytes = 256L * 1024 * 1024;

        private enum AudioEncoding
        {
            Wem = 1,
            Wave = 2
        }

        private readonly object _cacheLock = new();
        private readonly Dictionary<string, SoundEngineCacheEntry> _entriesByContentHash = new(StringComparer.Ordinal);
        private readonly IPackFileService _packFileService;
        private long _cachedBytes;
        private long _accessSequence;

        public SoundEngineCache(IPackFileService packFileService, long capacityBytes = DefaultCapacityBytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);
            _packFileService = packFileService;
            CapacityBytes = capacityBytes;
        }

        public long CapacityBytes { get; }
        internal long CachedBytes { get { lock (_cacheLock) return _cachedBytes; } }
        internal int CachedAudioCount { get { lock (_cacheLock) return _entriesByContentHash.Count; } }

        public SoundEngineCacheEntry GetWem(ReadOnlySpan<byte> wemBytes)
            => Get(wemBytes, AudioEncoding.Wem);

        public SoundEngineCacheEntry GetWave(ReadOnlySpan<byte> waveBytes)
            => Get(waveBytes, AudioEncoding.Wave);

        public SoundEngineCacheEntry GetFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            var packFile = _packFileService.FindFile(filePath)
                ?? throw new FileNotFoundException("Audio file was not found in the pack file service.", filePath);
            return GetWave(packFile.DataSource.ReadData());
        }

        private SoundEngineCacheEntry Get(ReadOnlySpan<byte> encodedBytes, AudioEncoding audioEncoding)
        {
            if (encodedBytes.IsEmpty)
                throw new ArgumentNullException(nameof(encodedBytes));

            var contentHash = ComputeContentHash(encodedBytes, audioEncoding);
            lock (_cacheLock)
            {
                if (_entriesByContentHash.TryGetValue(contentHash, out var cachedEntry))
                {
                    cachedEntry.LastAccessSequence = ++_accessSequence;
                    return cachedEntry;
                }

                var loadedEntry = Load(contentHash, encodedBytes.ToArray(), audioEncoding);
                loadedEntry.LastAccessSequence = ++_accessSequence;
                _entriesByContentHash.Add(contentHash, loadedEntry);
                _cachedBytes += loadedEntry.Size;
                EvictLeastRecentlyUsed(loadedEntry);
                return loadedEntry;
            }
        }

        private static SoundEngineCacheEntry Load(string contentHash, byte[] encodedBytes, AudioEncoding audioEncoding)
        {
            var sourceAudio = audioEncoding == AudioEncoding.Wem
                ? PcmAudio.CreateFromWemBytes(encodedBytes)
                : WavFile.CreateFromBytes(encodedBytes).Audio;

            return new SoundEngineCacheEntry(contentHash, ConvertForPlayback(sourceAudio));
        }

        private static PcmAudio ConvertForPlayback(PcmAudio sourceAudio)
        {
            if (sourceAudio.Data.Length == 0 || sourceAudio.Channels == 0 || sourceAudio.SampleRate == 0)
                throw new InvalidDataException("Audio has no decodable samples.");

            var sourceSamples = sourceAudio.ToInterleavedSamples();
            var sourceFrameCount = sourceSamples.Length / sourceAudio.Channels;
            if (sourceFrameCount == 0)
                throw new InvalidDataException("Audio has no complete sample frames.");
            var targetFrameCount = sourceAudio.SampleRate == PlaybackFormat.SampleRate
                ? sourceFrameCount
                : (int)Math.Ceiling(sourceFrameCount * (double)PlaybackFormat.SampleRate / sourceAudio.SampleRate);
            var targetSamples = new float[targetFrameCount * PlaybackFormat.ChannelCount];
            var sourceFramesPerTargetFrame = sourceAudio.SampleRate / (double)PlaybackFormat.SampleRate;
            for (var targetFrameIndex = 0; targetFrameIndex < targetFrameCount; targetFrameIndex++)
            {
                var sourceFramePosition = targetFrameIndex * sourceFramesPerTargetFrame;
                var precedingFrame = Math.Min((int)sourceFramePosition, sourceFrameCount - 1);
                var followingFrame = Math.Min(precedingFrame + 1, sourceFrameCount - 1);
                var interpolationAmount = (float)(sourceFramePosition - precedingFrame);
                for (var channelIndex = 0; channelIndex < PlaybackFormat.ChannelCount; channelIndex++)
                {
                    var sourceChannelIndex = Math.Min(channelIndex, sourceAudio.Channels - 1);
                    var precedingSample = sourceSamples[precedingFrame * sourceAudio.Channels + sourceChannelIndex];
                    var followingSample = sourceSamples[followingFrame * sourceAudio.Channels + sourceChannelIndex];
                    targetSamples[targetFrameIndex * PlaybackFormat.ChannelCount + channelIndex] =
                        precedingSample + (followingSample - precedingSample) * interpolationAmount;
                }
            }

            return PcmAudio.CreateFromFloatSamples(targetSamples, PlaybackFormat.ChannelCount, PlaybackFormat.SampleRate);
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

        private void EvictLeastRecentlyUsed(SoundEngineCacheEntry retainedEntry)
        {
            while (_cachedBytes > CapacityBytes && _entriesByContentHash.Count > 1)
            {
                var evictionCandidate = _entriesByContentHash.Values
                    .Where(entry => !ReferenceEquals(entry, retainedEntry))
                    .MinBy(entry => entry.LastAccessSequence);
                if (evictionCandidate == null)
                    return;

                _entriesByContentHash.Remove(evictionCandidate.ContentHash);
                _cachedBytes -= evictionCandidate.Size;
            }
        }
    }
}
