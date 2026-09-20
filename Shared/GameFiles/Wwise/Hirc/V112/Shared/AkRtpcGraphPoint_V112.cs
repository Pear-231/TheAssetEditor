using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V112.Shared
{
    public class AkRtpcGraphPoint_V112
    {
        public float From { get; set; }
        public float To { get; set; }
        public uint Interp { get; set; }

        public void ReadData(ByteChunk chunk)
        {
            From = chunk.ReadSingle();
            To = chunk.ReadSingle();
            Interp = chunk.ReadUInt32();
        }
    }
}
