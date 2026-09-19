using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    public class CAkFxCustom_V136 : HircItem
    {
        public uint PluginId { get; set; }
        public uint Size { get; set; }
        public AkPluginParam_V136 AkPluginParam { get; set; } = new AkPluginParam_V136();
        public byte NumBankData { get; set; }
        public List<AkMediaMap_V136> MediaList { get; set; } = [];
        public InitialRtpc_V136 InitialRtpc { get; set; } = new InitialRtpc_V136();
        public StateChunk_V136 StateChunk { get; set; } = new StateChunk_V136();
        public ushort NumValues { get; set; }
        public List<PluginPropertyValue_V136> PropertyValuesList { get; set; } = [];

        protected override void ReadData(ByteChunk chunk)
        {
            PluginId = chunk.ReadUInt32();
            Size = chunk.ReadUInt32();
            AkPluginParam = AkPluginParam_V136.ReadData(chunk, PluginId, Size);

            NumBankData = chunk.ReadByte();
            for (var i = 0; i < NumBankData; i++)
            {
                var akMediaMap = new AkMediaMap_V136();
                akMediaMap.ReadData(chunk);
                MediaList.Add(akMediaMap);
            }

            InitialRtpc.ReadData(chunk);
            StateChunk.ReadData(chunk);

            NumValues = chunk.ReadUShort();
            for (var i = 0; i < NumValues; i++)
            {
                var pluginPropertyValue = new PluginPropertyValue_V136();
                pluginPropertyValue.ReadData(chunk);
                PropertyValuesList.Add(pluginPropertyValue);
            }
        }

        public override byte[] WriteData() => throw new NotSupportedException("Users probably don't need this complexity.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Users probably don't need this complexity.");
    }
}
