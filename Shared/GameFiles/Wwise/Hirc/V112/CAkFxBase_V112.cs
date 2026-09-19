using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    // V112's FxBase keeps the older inline RTPC-initial-value list. The plug-in and media records
    // retain the same widths as the V136 base record.
    public abstract class CAkFxBase_V112 : HircItem
    {
        public uint PluginId { get; private set; }
        public uint PluginParameterSize { get; private set; }
        public AkPluginParam_V136 PluginParameters { get; private set; } = new();
        public List<AkMediaMap_V136> Media { get; } = [];
        public InitialRtpc_V112 InitialRtpc { get; } = new();
        public List<InitialValue_V112> InitialValues { get; } = [];

        protected override void ReadData(ByteChunk chunk)
        {
            PluginId = chunk.ReadUInt32();
            PluginParameterSize = chunk.ReadUInt32();
            PluginParameters = AkPluginParam_V136.ReadData(chunk, PluginId, PluginParameterSize);

            var mediaCount = chunk.ReadByte();
            for (var index = 0; index < mediaCount; index++)
            {
                var akMediaMap = new AkMediaMap_V136();
                akMediaMap.ReadData(chunk);
                Media.Add(akMediaMap);
            }

            InitialRtpc.ReadData(chunk);
            var valueCount = chunk.ReadUShort();
            for (var index = 0; index < valueCount; index++)
            {
                var initialValue = new InitialValue_V112();
                initialValue.ReadData(chunk);
                InitialValues.Add(initialValue);
            }
        }

        public override byte[] WriteData() => throw new NotSupportedException("Writing V112 effect HIRCs is not supported.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Writing V112 effect HIRCs is not supported.");

        public sealed class InitialValue_V112
        {
            public byte PropertyId { get; private set; }
            public float Value { get; private set; }
            public void ReadData(ByteChunk chunk)
            {
                PropertyId = chunk.ReadByte();
                Value = chunk.ReadSingle();
            }
        }
    }

    public sealed class CAkFxCustom_V112 : CAkFxBase_V112 { }
    public sealed class CAkFxShareSet_V112 : CAkFxBase_V112 { }
}
