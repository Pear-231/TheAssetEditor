using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc.V136.Shared
{
    public class AkMediaMap_V136
    {
        public byte Index { get; set; }
        public uint SourceId { get; set; }

        public void ReadData(ByteChunk chunk)
        {
            Index = chunk.ReadByte();
            SourceId = chunk.ReadUInt32();
        }
    }
}
