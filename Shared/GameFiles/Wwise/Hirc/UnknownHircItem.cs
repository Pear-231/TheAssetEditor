using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc
{
    public class UnknownHircItem : HircItem
    {
        // Only set when this type stands in for a HIRC item that threw while reading; absent when
        // it stands in for a raw type the factory simply has no registered reader for.
        public string? ErrorMsg { get; set; }

        protected override void ReadData(ByteChunk chunk)
        {
            chunk.ReadBytes((int)SectionSize - 4);
        }

        public override void UpdateSectionSize() => throw new NotImplementedException();
        public override byte[] WriteData() => throw new NotImplementedException();
    }
}
