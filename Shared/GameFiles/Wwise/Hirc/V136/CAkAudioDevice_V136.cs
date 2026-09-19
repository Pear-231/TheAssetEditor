using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    // Wwise 136 reads CAkAudioDevice as CAkFxBase: it does not carry CAkEffectSlots until V140.
    public sealed class CAkAudioDevice_V136 : HircItem
    {
        public uint PluginId { get; private set; }
        public uint PluginParameterSize { get; private set; }
        public AkPluginParam_V136 PluginParameters { get; private set; } = new();
        public List<AkMediaMap_V136> Media { get; } = [];
        public InitialRtpc_V136 InitialRtpc { get; } = new();
        public StateChunk_V136 StateChunk { get; } = new();
        public List<PluginPropertyValue_V136> PropertyValues { get; } = [];

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
            StateChunk.ReadData(chunk);
            var propertyValueCount = chunk.ReadUShort();
            for (var index = 0; index < propertyValueCount; index++)
            {
                var pluginPropertyValue = new PluginPropertyValue_V136();
                pluginPropertyValue.ReadData(chunk);
                PropertyValues.Add(pluginPropertyValue);
            }
        }

        public override byte[] WriteData() => throw new NotSupportedException("Writing audio-device HIRCs is not supported.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Writing audio-device HIRCs is not supported.");
    }
}
