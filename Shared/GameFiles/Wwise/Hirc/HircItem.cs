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
        public bool IsTarget { get; set; }
        public List<HircItem>? HircChildren { get; set; }
        public HircHeader Header { get; set; } = new HircHeader();
        public AkBkHircType HircType { get; set; }
        public uint SectionSize { get => Header.SectionSize; set => Header.SectionSize = value; }
        public uint Id { get => Header.Id; set => Header.Id = value; }

        public void ReadHirc(ByteChunk chunk, BankVersion bankVersion)
        {
            try
            {
                var indexBeforeRead = chunk.Index;
                ByteIndexInFile = (uint)indexBeforeRead;

                Header.ReadData(chunk);
                ReadData(chunk, bankVersion);

                var indexAfterRead = (int)(indexBeforeRead + HircHeader.PrefixSize + SectionSize);
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

        protected abstract void ReadData(ByteChunk chunk, BankVersion bankVersion);
        public abstract byte[] WriteData(BankVersion bankVersion);
        public abstract void UpdateSectionSize(); 
    }
}
