using Editors.Audio.Shared.Wwise.Engine.Hierarchy;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Enums;

namespace Editors.Audio.Shared.Storage
{
    // The engine's view of the loaded banks, over the repository that already indexes them.
    //
    // This sits outside the engine on purpose: the engine owns the hierarchy — the walk, the
    // container rules and the media — while pack files, bank layering and the on-disk cache stay
    // the repository's, and neither has to know how the other works.
    public sealed class AudioRepositoryHierarchy : IHierarchyProvider
    {
        private readonly ILogger _logger = Logging.Create<AudioRepositoryHierarchy>();
        private readonly IAudioRepository _audioRepository;
        private readonly Func<byte[], SourceMedia> _loadWem;

        public AudioRepositoryHierarchy(IAudioRepository audioRepository, MediaCache mediaCache)
        {
            ArgumentNullException.ThrowIfNull(audioRepository);
            ArgumentNullException.ThrowIfNull(mediaCache);
            _audioRepository = audioRepository;
            _loadWem = wemBytes => mediaCache.GetWem(wemBytes);
        }

        internal AudioRepositoryHierarchy(IAudioRepository audioRepository, Func<byte[], SourceMedia> loadWem)
        {
            ArgumentNullException.ThrowIfNull(audioRepository);
            ArgumentNullException.ThrowIfNull(loadWem);
            _audioRepository = audioRepository;
            _loadWem = loadWem;
        }

        public HircItem FindEvent(string eventName) => _audioRepository.FindActionEvent(eventName);

        // Preferring the bank the reference was written in, because the same id can be defined in
        // more than one bank and a reference means the one next to it.
        public HircItem FindNode(uint nodeId, string referringBnkFilePath)
        {
            var nodeHircItems = _audioRepository.GetHircs(nodeId);
            if (nodeHircItems.Count == 0)
                return null;

            return nodeHircItems.FirstOrDefault(hircItem =>
                    string.Equals(hircItem.BnkFilePath, referringBnkFilePath, StringComparison.OrdinalIgnoreCase))
                ?? nodeHircItems[0];
        }

        public string GetName(uint id) => _audioRepository.GetNameFromId(id);

        public SourceMedia FindMedia(uint sourceId, string referringBnkFilePath) => LoadMedia(sourceId, referringBnkFilePath);

        public IReadOnlyList<HircItem> GetMasterMixerNodes()
            => _audioRepository.GetHircs(AkBkHircType.Audio_Bus)
                .Concat(_audioRepository.GetHircs(AkBkHircType.AuxiliaryBus))
                .ToArray();

        private SourceMedia LoadMedia(uint sourceId, string referringBnkFilePath)
        {
            var wemBytes = FindWemBytes(sourceId, referringBnkFilePath, out var wemByteSource);
            if (wemBytes == null)
                return null;

            try
            {
                var media = _loadWem(wemBytes);

                // Detail rather than news, so it sits at debug: one line per file is a wall of log
                // on a unit with many events. It is here at all because this is the one step
                // nothing downstream can check — a WEM that decodes to the wrong thing still
                // reports a duration and still plays — and because a bank and a pack file can both
                // answer to one source id while only one of them is the audio that was meant.
                _logger.Here().Debug(
                    $"{sourceId}.wem loaded from {wemByteSource}: {wemBytes.Length} encoded bytes decoded to " +
                    $"{media.SampleRate} Hz, {media.ChannelCount} channel(s), {media.Duration.TotalMilliseconds:F0} ms");
                return media;
            }
            catch (Exception exception)
            {
                _logger.Here().Warning(exception, $"{sourceId}.wem could not be decoded");
                return null;
            }
        }

        private byte[] FindWemBytes(uint sourceId, string referringBnkFilePath, out string wemByteSource)
        {
            wemByteSource = "nowhere";
            // Embedded in a bank first, and the bank the sound was defined in before any other,
            // because two banks can carry different audio under one source id.
            var embeddedWemMatches = _audioRepository.FindDidxWem(sourceId);
            if (embeddedWemMatches.Count != 0)
            {
                var matchingBankWem = embeddedWemMatches.FirstOrDefault(embeddedWem =>
                    string.Equals(embeddedWem.OwnerFilePath, referringBnkFilePath, StringComparison.OrdinalIgnoreCase));
                var embeddedWem = matchingBankWem ?? embeddedWemMatches[0];
                wemByteSource = $"the bank '{embeddedWem.OwnerFilePath}' ({embeddedWemMatches.Count} bank(s) carry it)";
                return embeddedWem.ByteArray;
            }

            var wemPackFile = _audioRepository.FindWem(sourceId.ToString());
            if (wemPackFile != null)
            {
                wemByteSource = $"the pack file '{wemPackFile.Name}'";
                return wemPackFile.DataSource.ReadData();
            }

            _logger.Here().Warning($"{sourceId}.wem is referenced by a loaded bank but is not in any pack file");
            return null;
        }
    }
}
