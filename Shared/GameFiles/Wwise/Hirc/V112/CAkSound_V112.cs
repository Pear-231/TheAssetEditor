using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    public class CAkSound_V112 : HircItem, ICAkSound
    {
        public AkBankSourceData_V112 AkBankSourceData { get; set; }
        public NodeBaseParams_V112 NodeBaseParams { get; set; } = new NodeBaseParams_V112();

        protected override void ReadData(ByteChunk chunk, BankVersion bankVersion)
        {
            AkBankSourceData.ReadData(chunk, bankVersion);
            NodeBaseParams.ReadData(chunk, bankVersion);
        }

        public override byte[] WriteData(BankVersion bankVersion)
        {
            using var memStream = WriteHeader();
            memStream.Write(AkBankSourceData.WriteData());
            memStream.Write(NodeBaseParams.WriteData(bankVersion));
            var byteArray = memStream.ToArray();

            // Reload the object to ensure sanity
            var sanityReload = new CAkSound_V112();
            sanityReload.ReadHirc(new ByteChunk(byteArray), bankVersion);

            return byteArray;
        }

        public override void UpdateSectionSize()
        {
            var idSize = ByteHelper.GetPropertyTypeSize(Id);
            var akBankSourceDataSize = AkBankSourceData.GetSize();
            var nodeBaseParamsSize = NodeBaseParams.GetSize();
            SectionSize = idSize + akBankSourceDataSize + nodeBaseParamsSize;
        }

        public uint GetDirectParentId() => NodeBaseParams.DirectParentId;
        public uint GetSourceId() => AkBankSourceData.AkMediaInformation.SourceId;
        public AKBKSourceType GetStreamType() => AkBankSourceData.StreamType;
    }
}
