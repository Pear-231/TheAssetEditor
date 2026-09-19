using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc.V136.Shared
{
    public class NodeBaseParams_V136 : INodeBaseParams
    {
        // Bit 0 says the node states its own priority rather than inheriting its parent's, and bit 1
        // says that priority is scaled by distance. Measured across both games: where bit 0 is set a
        // priority is authored 87% (Warhammer III) and 99% (Attila) of the time, and where bit 1 is
        // set a priority distance offset is authored 89% and 94% of the time against 0.7% and 6.6%
        // where it is clear. Bits 2 to 7 are never set in either game.
        private const byte PriorityOverridesParentBit = 0x01;
        private const byte PriorityAppliesDistanceFactorBit = 0x02;

        public NodeInitialFxParams_V136 NodeInitialFxParams { get; set; } = new NodeInitialFxParams_V136();
        public byte OverrideAttachmentParams { get; set; }
        public uint OverrideBusId { get; set; }
        public uint DirectParentId { get; set; }
        public byte BitVector { get; set; }
        public NodeInitialParams_V136 NodeInitialParams { get; set; } = new NodeInitialParams_V136();
        public PositioningParams_V136 PositioningParams { get; set; } = new PositioningParams_V136();
        public AuxParams_V136 AuxParams { get; set; } = new AuxParams_V136();
        public AdvSettingsParams_V136 AdvSettingsParams { get; set; } = new AdvSettingsParams_V136();
        public StateChunk_V136 StateChunk { get; set; } = new StateChunk_V136();
        public InitialRtpc_V136 InitialRtpc { get; set; } = new InitialRtpc_V136();

        public bool OverridesParentPriority => (BitVector & PriorityOverridesParentBit) != 0;

        // Nothing reads the offset yet, because nothing positions a voice: this is here so the
        // engine can say it is not applying an offset rather than silently applying one that the
        // node never asked to have applied.
        public bool AppliesPriorityDistanceFactor => (BitVector & PriorityAppliesDistanceFactorBit) != 0;

        public ushort MaxInstanceCount => AdvSettingsParams.MaxNumInstance;
        public bool IsGlobalLimit => AdvSettingsParams.IsGlobalLimit;
        public bool DiscardsNewestOnLimit => AdvSettingsParams.DiscardsNewest;
        public bool UsesVirtualVoiceOnLimit => AdvSettingsParams.UsesVirtualVoice;
        public bool IsPositioned => PositioningParams.IsPositioned;
        public byte VirtualQueueBehaviour => AdvSettingsParams.VirtualQueueBehavior;
        public byte BelowThresholdBehaviour => AdvSettingsParams.BelowThresholdBehavior;

        // This version has no attenuation field in its positioning params — it authors the
        // attenuation as a property bundle entry instead, gated above bank version 131, which is
        // where `AuthoredParameters` looks before falling back to this.
        public uint AttenuationId => 0;

        public AuthoredProperties GetAuthoredProperties()
            => WwisePropertyMap_V136.Read(NodeInitialParams.AkPropBundle0, NodeInitialParams.AkPropBundle1);

        public void ReadData(ByteChunk chunk)
        {
            NodeInitialFxParams.ReadData(chunk);
            OverrideAttachmentParams = chunk.ReadByte();
            OverrideBusId = chunk.ReadUInt32();
            DirectParentId = chunk.ReadUInt32();
            BitVector = chunk.ReadByte();
            NodeInitialParams.ReadData(chunk);
            PositioningParams.ReadData(chunk);
            AuxParams.ReadData(chunk);
            AdvSettingsParams.ReadData(chunk);
            StateChunk.ReadData(chunk);
            InitialRtpc.ReadData(chunk);
        }

        public byte[] WriteData()
        {
            using var memStream = new MemoryStream();
            memStream.Write(NodeInitialFxParams.WriteData());
            memStream.Write(ByteParsers.Byte.EncodeValue(OverrideAttachmentParams, out _));
            memStream.Write(ByteParsers.UInt32.EncodeValue(OverrideBusId, out _));
            memStream.Write(ByteParsers.UInt32.EncodeValue(DirectParentId, out _));
            memStream.Write(ByteParsers.Byte.EncodeValue(BitVector, out _));
            memStream.Write(NodeInitialParams.WriteData());
            memStream.Write(PositioningParams.WriteData());
            memStream.Write(AuxParams.WriteData());
            memStream.Write(AdvSettingsParams.WriteData());
            memStream.Write(StateChunk.WriteData());
            memStream.Write(InitialRtpc.WriteData());
            return memStream.ToArray();
        }

        public uint GetSize()
        {
            var overrideAttachmentSize = ByteHelper.GetPropertyTypeSize(OverrideAttachmentParams);
            var overrideBusIdSize = ByteHelper.GetPropertyTypeSize(OverrideBusId);
            var directParentId = ByteHelper.GetPropertyTypeSize(DirectParentId);
            var bitVectorId = ByteHelper.GetPropertyTypeSize(BitVector);

            return NodeInitialFxParams.GetSize() + (overrideAttachmentSize + overrideBusIdSize + directParentId + bitVectorId)
                   + NodeInitialParams.GetSize() + PositioningParams.GetSize() + AuxParams.GetSize() + AdvSettingsParams.GetSize() + StateChunk.GetSize() + InitialRtpc.GetSize();
        }
    }
}
