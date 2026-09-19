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
            {
                var cAkSwitchPackage = new CAkSwitchPackage_V112();
                cAkSwitchPackage.ReadData(chunk);
                SwitchList.Add(cAkSwitchPackage);
            }

            var paramCount = chunk.ReadUInt32();
            for (var i = 0; i < paramCount; i++)
            {
                var akSwitchNodeParams = new AkSwitchNodeParams_V112();
                akSwitchNodeParams.ReadData(chunk);
                Parameters.Add(akSwitchNodeParams);
            }
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

            public void ReadData(ByteChunk chunk)
            {
                SwitchId = chunk.ReadUInt32();
                var numChildren = chunk.ReadUInt32();
                for (var i = 0; i < numChildren; i++)
                    NodeIdList.Add(chunk.ReadUInt32());
            }
        }

        public class AkSwitchNodeParams_V112 : IAkSwitchNodeParams
        {
            public uint NodeId { get; set; }
            public byte BitVector0 { get; set; }
            public byte BitVector1 { get; set; }
            public int FadeOutTime { get; set; }
            public int FadeInTime { get; set; }

            public void ReadData(ByteChunk chunk)
            {
                NodeId = chunk.ReadUInt32();
                BitVector0 = chunk.ReadByte();
                BitVector1 = chunk.ReadByte();
                FadeOutTime = chunk.ReadInt32();
                FadeInTime = chunk.ReadInt32();
            }
        }
        INodeBaseParams ICAkParameterNode.NodeBaseParams => NodeBaseParams;
    }
}
