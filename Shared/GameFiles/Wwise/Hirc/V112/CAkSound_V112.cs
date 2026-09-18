using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    public class CAkSound_V112 : HircItem, ICAkSound, ICAkParameterNode
    {
        public AkBankSourceData_V112 AkBankSourceData { get; set; }
        public NodeBaseParams_V112 NodeBaseParams { get; set; } = new NodeBaseParams_V112();

        protected override void ReadData(ByteChunk chunk)
        {
            AkBankSourceData = AkBankSourceData_V112.ReadData(chunk);
            NodeBaseParams.ReadData(chunk);
        }

        public override byte[] WriteData()
        {
            using var memStream = WriteHeader();
            memStream.Write(AkBankSourceData.WriteData());
            memStream.Write(NodeBaseParams.WriteData());
            var byteArray = memStream.ToArray();

            // Reload the object to ensure sanity
            var sanityReload = new CAkSound_V112();
            sanityReload.ReadHirc(new ByteChunk(byteArray));

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
        public ushort GetMaxInstanceCount() => NodeBaseParams.AdvSettingsParams.MaxNumInstance;
        public AuthoredProperties GetProperties() => NodeBaseParams.GetAuthoredProperties();
        public bool GetOverridesParentPriority() => NodeBaseParams.OverridesParentPriority;
        public bool GetIsGlobalLimit() => NodeBaseParams.AdvSettingsParams.IsGlobalLimit;
        public bool GetDiscardsNewestOnLimit() => NodeBaseParams.AdvSettingsParams.DiscardsNewest;
        public bool GetUsesVirtualVoiceOnLimit() => NodeBaseParams.AdvSettingsParams.UsesVirtualVoice;
        public uint GetOutputBusId() => NodeBaseParams.OverrideBusId;
        public bool GetIsPositioned() => (NodeBaseParams.PositioningParams.ByVector & 0x09) == 0x09;
        public byte GetVirtualQueueBehaviour() => NodeBaseParams.AdvSettingsParams.VirtualQueueBehavior;
        public byte GetBelowThresholdBehaviour() => NodeBaseParams.AdvSettingsParams.BelowThresholdBehavior;
        public uint GetAttenuationId() => NodeBaseParams.PositioningParams.AttenuationId;
    }
}
