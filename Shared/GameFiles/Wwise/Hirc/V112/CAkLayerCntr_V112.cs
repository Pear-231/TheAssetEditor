using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    public class CAkLayerCntr_V112 : HircItem, ICAkLayerCntr, ICAkParameterNode
    {
        public NodeBaseParams_V112 NodeBaseParams { get; set; } = new NodeBaseParams_V112();
        public Children_V112 Children { get; set; } = new Children_V112();
        public uint NumLayers { get; set; }
        public List<CAkLayer_V112> LayerList { get; set; } = [];
        public byte IsContinuousValidation { get; set; }

        protected override void ReadData(ByteChunk chunk)
        {
            NodeBaseParams.ReadData(chunk);
            Children.ReadData(chunk);

            NumLayers = chunk.ReadUInt32();
            for (var i = 0; i < NumLayers; i++)
            {
                var layer = new CAkLayer_V112();
                layer.ReadData(chunk);
                LayerList.Add(layer);
            }
        }

        public override byte[] WriteData() => throw new NotSupportedException("Users probably don't need this complexity.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Users probably don't need this complexity.");

        public List<uint> GetChildren() => Children.ChildIds;
        public uint GetDirectParentId() => NodeBaseParams.DirectParentId;
        public bool GetIsContinuousValidation() => IsContinuousValidation != 0;
        public IReadOnlyList<ICAkLayerCntr.IAkLayer> GetLayers() => LayerList;

        public class CAkLayer_V112 : ICAkLayerCntr.IAkLayer
        {
            public uint UlLayerIr { get; set; }
            public InitialRtpc_V112 InitialRtpc { get; set; } = new InitialRtpc_V112();
            public uint RtpcId { get; set; }
            public AkRtpcType RtpcType { get; set; }
            public uint NumAssoc {  get; set; }
            public List<CAssociatedChildData_V112> CAssociatedChildDataList { get; set; } = [];

            public void ReadData(ByteChunk chunk)
            {
                UlLayerIr = chunk.ReadUInt32();
                InitialRtpc.ReadData(chunk);
                RtpcId = chunk.ReadUInt32();
                RtpcType = (AkRtpcType)chunk.ReadByte();

                NumAssoc = chunk.ReadUInt32();
                for (var i = 0; i < NumAssoc; i++)
                {
                    var associatedChildData = new CAssociatedChildData_V112();
                    associatedChildData.ReadData(chunk);
                    CAssociatedChildDataList.Add(associatedChildData);
                }
            }


            public IReadOnlyList<ICAkLayerCntr.IAkAssociatedChildData> GetAssociatedChildren() => CAssociatedChildDataList;
        }

        public class CAssociatedChildData_V112 : ICAkLayerCntr.IAkAssociatedChildData
        {
            public uint AssociatedChildId { get; set; }
            public uint CurveSize { get; set; }
            public List<AkRtpcGraphPoint_V112> AkRtpcGraphPointList { get; set; } = [];

            public void ReadData(ByteChunk chunk)
            {
                AssociatedChildId = chunk.ReadUInt32();
                CurveSize = chunk.ReadUInt32();
                for (var i = 0; i < CurveSize; i++)
                    AkRtpcGraphPointList.Add(AkRtpcGraphPoint_V112.ReadData(chunk));
            }


            public IReadOnlyList<ICAkLayerCntr.IAkRtpcGraphPoint> GetCurvePoints() => AkRtpcGraphPointList;
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
