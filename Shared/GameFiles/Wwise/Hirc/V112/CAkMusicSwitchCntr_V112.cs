using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    public sealed class CAkMusicSwitchCntr_V112 : HircItem
    {
        public MusicTransNodeParams_V112 MusicTransNodeParams { get; } = new();
        public byte ContinuePlayback { get; private set; }
        public uint TreeDepth { get; private set; }
        public List<AkGameSync_V112> Arguments { get; } = [];
        public uint TreeDataSize { get; private set; }
        public byte Mode { get; private set; }
        public AkDecisionTree_V112 DecisionTree { get; } = new();

        protected override void ReadData(ByteChunk chunk)
        {
            MusicTransNodeParams.ReadData(chunk);
            ContinuePlayback = chunk.ReadByte();
            TreeDepth = chunk.ReadUInt32();
            for (var index = 0; index < TreeDepth; index++) Arguments.Add(new AkGameSync_V112());
            for (var index = 0; index < TreeDepth; index++) Arguments[index].GroupId = chunk.ReadUInt32();
            for (var index = 0; index < TreeDepth; index++) Arguments[index].GroupType = (AkGroupType)chunk.ReadByte();
            TreeDataSize = chunk.ReadUInt32();
            Mode = chunk.ReadByte();
            DecisionTree.ReadData(chunk, TreeDataSize, TreeDepth);
        }

        public override byte[] WriteData() => throw new NotSupportedException("Writing music switch HIRCs is not supported.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Writing music switch HIRCs is not supported.");
    }
}
