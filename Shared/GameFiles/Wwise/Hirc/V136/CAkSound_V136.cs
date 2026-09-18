using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    public class CAkSound_V136 : HircItem, ICAkSound, ICAkParameterNode
    {
        public AkBankSourceData_V136 AkBankSourceData { get; set; }
        public NodeBaseParams_V136 NodeBaseParams { get; set; } = new NodeBaseParams_V136();

        protected override void ReadData(ByteChunk chunk)
        {
            AkBankSourceData = AkBankSourceData_V136.ReadData(chunk);
            NodeBaseParams.ReadData(chunk);
        }

        public override byte[] WriteData()
        {
            using var memStream = WriteHeader();
            memStream.Write(AkBankSourceData.WriteData());
            memStream.Write(NodeBaseParams.WriteData());
            var byteArray = memStream.ToArray();

            // Reload the object to ensure sanity
            var sanityReload = new CAkSound_V136();
            sanityReload.ReadHirc(new ByteChunk(byteArray));

            return byteArray;
        }

        public override void UpdateSectionSize()
        {
            var idSize = ByteHelper.GetPropertyTypeSize(Id);
            SectionSize = idSize + AkBankSourceData.GetSize() + NodeBaseParams.GetSize();
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
        public bool GetIsPositioned() => (NodeBaseParams.PositioningParams.BitsPositioning & 0x03) == 0x03;
        public byte GetVirtualQueueBehaviour() => NodeBaseParams.AdvSettingsParams.VirtualQueueBehavior;
        public byte GetBelowThresholdBehaviour() => NodeBaseParams.AdvSettingsParams.BelowThresholdBehavior;
    }
}
