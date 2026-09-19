using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Data
{
    public class DataChunk
    {
        public ChunkHeader ChunkHeader { get; set; } = new ChunkHeader();
        public ByteChunk Data { get; set; } = new ByteChunk([]);

        public void ReadData(string fileName, ByteChunk chunk)
        {
            ChunkHeader.ReadData(chunk);
            Data = chunk.CreateSub((int)ChunkHeader.ChunkSize);
        }
    }
}
