using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;
using static Shared.GameFormats.Wwise.Hirc.ICAkSwitchCntr;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    public class CAkSwitchCntr_V112 : HircItem, ICAkSwitchCntr, ICAkParameterNode
    {
        public NodeBaseParams_V112 NodeBaseParams { get; set; } = new NodeBaseParams_V112();
        public AkGroupType GroupType { get; set; }
        public uint GroupId { get; set; }
        public uint DefaultSwitch { get; set; }
        public byte IsContinuousValidation { get; set; }
        public Children_V112 Children { get; set; } = new Children_V112();
        public List<ICAkSwitchPackage> SwitchList { get; set; } = [];
        public List<AkSwitchNodeParams_V112> Parameters { get; set; } = [];

        protected override void ReadData(ByteChunk chunk)
        {
            NodeBaseParams.ReadData(chunk);
            GroupType = (AkGroupType)chunk.ReadByte();
            GroupId = chunk.ReadUInt32();
            DefaultSwitch = chunk.ReadUInt32();
            IsContinuousValidation = chunk.ReadByte();
            Children.ReadData(chunk);

            var switchListCount = chunk.ReadUInt32();
            for (var i = 0; i < switchListCount; i++)
                SwitchList.Add(CAkSwitchPackage_V112.ReadData(chunk));

            var paramCount = chunk.ReadUInt32();
            for (var i = 0; i < paramCount; i++)
                Parameters.Add(AkSwitchNodeParams_V112.ReadData(chunk));
        }

        public override byte[] WriteData() => throw new NotSupportedException("Users probably don't need this complexity.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Users probably don't need this complexity.");
        public uint GetDirectParentId() => NodeBaseParams.DirectParentId;
        public AkGroupType GetGroupType() => GroupType;
        public bool GetIsContinuousValidation() => IsContinuousValidation != 0;
        public IReadOnlyList<IAkSwitchNodeParams> GetNodeParameters() => Parameters;

        public class CAkSwitchPackage_V112 : ICAkSwitchPackage
        {
            public uint SwitchId { get; set; }
            public List<uint> NodeIdList { get; set; } = [];

            public static ICAkSwitchPackage ReadData(ByteChunk chunk)
            {
                var instance = new CAkSwitchPackage_V112();
                instance.SwitchId = chunk.ReadUInt32();
                var numChildren = chunk.ReadUInt32();
                for (var i = 0; i < numChildren; i++)
                    instance.NodeIdList.Add(chunk.ReadUInt32());
                return instance;
            }
        }

        public class AkSwitchNodeParams_V112 : IAkSwitchNodeParams
        {
            public uint NodeId { get; set; }
            public byte BitVector0 { get; set; }
            public byte BitVector1 { get; set; }
            public int FadeOutTime { get; set; }
            public int FadeInTime { get; set; }

            public static AkSwitchNodeParams_V112 ReadData(ByteChunk chunk)
            {
                var instance = new AkSwitchNodeParams_V112();
                instance.NodeId = chunk.ReadUInt32();
                instance.BitVector0 = chunk.ReadByte();
                instance.BitVector1 = chunk.ReadByte();
                instance.FadeOutTime = chunk.ReadInt32();
                instance.FadeInTime = chunk.ReadInt32();
                return instance;
            }
        }
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
