using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Didx
{
    public partial class DidxChunk
    {
        public ChunkHeader ChunkHeader { get; set; } = new ChunkHeader();
        public List<MediaHeader> MediaList { get; set; } = [];

        public void ReadData(string fileName, ByteChunk chunk)
        {
            ChunkHeader.ReadData(chunk);
            MediaList = ReadMediaHeaders(chunk, ChunkHeader.ChunkSize);
        }

        public static List<MediaHeader> ReadMediaHeaders(ByteChunk chunk, uint chunkSize)
        {
            if (chunkSize % MediaHeader.ByteSize != 0)
                throw new InvalidDataException($"DIDX chunk size {chunkSize} is not a multiple of {MediaHeader.ByteSize}.");

            var items = chunkSize / MediaHeader.ByteSize;
            var mediaHeaders = new List<MediaHeader>((int)items);
            for (var itemIndex = 0; itemIndex < items; itemIndex++)
            {
                var mediaHeader = new MediaHeader();
                mediaHeader.ReadData(chunk);
                mediaHeaders.Add(mediaHeader);
            }

            return mediaHeaders;
        }
    }
}
