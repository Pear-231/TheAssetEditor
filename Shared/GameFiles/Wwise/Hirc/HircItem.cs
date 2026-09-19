using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc
{
    public abstract class HircItem
    {
        readonly ILogger _logger = Logging.Create<HircItem>();

        public string BnkFilePath { get; set; } = "Not Set";
        public bool IsCA { get; set; }
        public uint LanguageId { get; set; } 
        public uint ByteIndexInFile { get; set; }
        public uint IndexInFile { get; set; }
        public bool HasError { get; set; } = true;

        // What the object left unread. Reading has to reposition to the size the bank states,
        // because a type this project does not parse in full still has to be stepped over -- but
        // that repositioning is also what hides a field read at the wrong width. Recording the
        // difference is what makes such a misread findable at all: a type that claims to parse an
        // object in full should leave nothing behind, and a negative count means it read past the
        // object entirely.
        public int UnreadByteCount { get; private set; }
        public bool IsTarget { get; set; }
        public List<HircItem>? HircChildren { get; set; }
        public HircHeader Header { get; set; } = new HircHeader();
        public AkBkHircType HircType { get => Header.HircType; set => Header.HircType = value; }
        public uint SectionSize { get => Header.SectionSize; set => Header.SectionSize = value; }
        public uint Id { get => Header.Id; set => Header.Id = value; }

        public static HircItem ReadData(
            string filePath,
            ByteChunk chunk,
            uint bankGeneratorVersion,
            uint languageId,
            bool isCA,
            uint itemIndex,
            int? expectedLength = null)
            => ReadData(filePath, chunk, WwiseVersionResolver.Resolve(bankGeneratorVersion), languageId, isCA, itemIndex, expectedLength);

        public static HircItem ReadData(
            string filePath,
            ByteChunk chunk,
            WwiseVersionDefinition versionDefinition,
            uint languageId,
            bool isCA,
            uint itemIndex,
            int? expectedLength = null)
        {
            if (expectedLength.HasValue && expectedLength.Value < HircHeader.Size)
                throw new InvalidDataException($"HIRC item {itemIndex} is only {expectedLength.Value} bytes.");

            var itemStartIndex = chunk.Index;
            var hircType = versionDefinition.DecodeHircType(chunk.PeakByte());
            HircItem hircItem;

            try
            {
                hircItem = versionDefinition.CreateHirc(hircType);
                hircItem.IndexInFile = itemIndex;
                hircItem.ByteIndexInFile = itemIndex;
                hircItem.BnkFilePath = filePath;
                hircItem.LanguageId = languageId;
                hircItem.IsCA = isCA;
                hircItem.ReadHirc(chunk);
                hircItem.HircType = hircType;
            }
            catch (Exception exception)
            {
                chunk.Index = itemStartIndex;

                hircItem = new UnknownHircItem
                {
                    ErrorMsg = exception.Message,
                    ByteIndexInFile = itemIndex,
                    BnkFilePath = filePath
                };
                hircItem.ReadHirc(chunk);
                hircItem.HircType = hircType;
            }

            var bytesRead = chunk.Index - itemStartIndex;
            if (expectedLength.HasValue && bytesRead != expectedLength.Value)
                throw new InvalidDataException($"HIRC item {itemIndex} expected {expectedLength.Value} bytes but read {bytesRead}.");

            return hircItem;
        }

        private static AkBkHircType ResolveHircType(byte rawHircType, uint bankGeneratorVersion)
        {
            if (bankGeneratorVersion > 126)
                return (AkBkHircType)rawHircType;

            return rawHircType switch
            {
                0x10 => AkBkHircType.FeedbackBus,
                0x11 => AkBkHircType.FeedbackNode,
                0x12 => AkBkHircType.FxShareSet,
                0x13 => AkBkHircType.FxCustom,
                0x14 => AkBkHircType.AuxiliaryBus,
                0x15 => AkBkHircType.LFO,
                0x16 => AkBkHircType.Envelope,
                0x17 => AkBkHircType.AudioDevice,
                _ => (AkBkHircType)rawHircType
            };
        }

        internal static byte EncodeHircType(AkBkHircType hircType, uint bankGeneratorVersion)
        {
            if (bankGeneratorVersion > 126)
                return (byte)hircType;

            return hircType switch
            {
                AkBkHircType.FeedbackBus => 0x10,
                AkBkHircType.FeedbackNode => 0x11,
                AkBkHircType.FxShareSet => 0x12,
                AkBkHircType.FxCustom => 0x13,
                AkBkHircType.AuxiliaryBus => 0x14,
                AkBkHircType.LFO => 0x15,
                AkBkHircType.Envelope => 0x16,
                AkBkHircType.AudioDevice => 0x17,
                _ => (byte)hircType
            };
        }

        public void ReadHirc(ByteChunk chunk)
        {
            try
            {
                var indexBeforeRead = chunk.Index;
                ByteIndexInFile = (uint)indexBeforeRead;

                Header.ReadData(chunk);
                ReadData(chunk);

                var indexAfterRead = (int)(indexBeforeRead + HircHeader.PrefixSize + SectionSize);
                UnreadByteCount = indexAfterRead - chunk.Index;
                chunk.Index = indexAfterRead;
                HasError = false;
            }

            catch (Exception e)
            {
                _logger.Here().Error($"Failed to parse object {Id} of type {HircType} in {BnkFilePath} at index {IndexInFile} - " + e.Message);
                throw;
            }
        }

        protected MemoryStream WriteHeader()
        {
            var memStream = new MemoryStream();
            memStream.Write(HircHeader.WriteData(Header));
            return memStream;
        }

        protected abstract void ReadData(ByteChunk chunk);
        public abstract byte[] WriteData();
        public abstract void UpdateSectionSize(); 
    }
}
