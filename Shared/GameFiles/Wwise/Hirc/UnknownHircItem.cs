using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc
{
    public class UnknownHircItem : HircItem
    {
        public string ErrorMsg { get; set; } = string.Empty;

        protected override void ReadData(ByteChunk chunk, BankVersion bankVersion)
        {
            chunk.ReadBytes((int)SectionSize - 4);
        }

        public override void UpdateSectionSize() => throw new NotImplementedException();
        public override byte[] WriteData(BankVersion bankVersion) => throw new NotImplementedException();
    }
}
