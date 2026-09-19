using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Wem.V132
{
    public class RiffChunkHeader(string tag = "", uint chunkSize = 0)
    {
        public const uint HeaderSize = 8;
        public const int ChunkPaddingAlignment = 2;

        public string Tag { get; set; } = tag;
        public uint ChunkSize { get; set; } = chunkSize;

        public void ReadData(ByteChunk chunk)
        {
            Tag = System.Text.Encoding.ASCII.GetString(chunk.ReadBytes(4));
            ChunkSize = chunk.ReadUInt32();
        }

        public static RiffChunkHeader PeekFromBytes(ByteChunk chunk)
        {
            var peekBytes = chunk.PeekChunk((int)HeaderSize);
            var header = new RiffChunkHeader();
            header.ReadData(peekBytes);
            return header;
        }

        public static byte[] WriteData(RiffChunkHeader header)
        {
            if (header.Tag.Length != 4)
                throw new Exception($"Header not valid {header.Tag}");

            using var stream = new MemoryStream();
            stream.Write(ByteParsers.Byte.EncodeValue((byte)header.Tag[0], out _));
            stream.Write(ByteParsers.Byte.EncodeValue((byte)header.Tag[1], out _));
            stream.Write(ByteParsers.Byte.EncodeValue((byte)header.Tag[2], out _));
            stream.Write(ByteParsers.Byte.EncodeValue((byte)header.Tag[3], out _));
            stream.Write(ByteParsers.UInt32.EncodeValue(header.ChunkSize, out _));

            var byteArray = stream.ToArray();
            return byteArray;
        }
    }
}
