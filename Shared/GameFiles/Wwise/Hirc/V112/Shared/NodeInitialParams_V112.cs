using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc.V112.Shared
{
    public class NodeInitialParams_V112
    {
        public AkPropBundle_V112 AkPropBundle0 { get; set; } = new AkPropBundle_V112();
        public AkPropBundleMinMax_V112 AkPropBundle1 { get; set; } = new AkPropBundleMinMax_V112();

        public void ReadData(ByteChunk chunk, BankVersion bankVersion)
        {
            AkPropBundle0.ReadData(chunk, bankVersion);
            AkPropBundle1.ReadData(chunk, bankVersion);
        }

        public byte[] WriteData(BankVersion bankVersion)
        {
            using var memStream = new MemoryStream();
            memStream.Write(AkPropBundle0.WriteData(bankVersion));
            memStream.Write(AkPropBundle1.WriteData(bankVersion));
            return memStream.ToArray();
        }

        public uint GetSize() => AkPropBundle0.GetSize() + AkPropBundle1.GetSize();
    }
}
