using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc.V136.Shared
{
    public class PluginPropertyValue_V136
    {
        public uint PropertyId { get; set; }
        public byte RtpcAccum { get; set; }
        public float Value { get; set; }

        public void ReadData(ByteChunk chunk)
        {
            PropertyId = WwiseVariableUInt32Parser.Read(chunk);
            RtpcAccum = chunk.ReadByte();
            Value = chunk.ReadSingle();
        }
    }
}
