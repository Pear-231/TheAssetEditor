using System.Text;
using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise
{
    public class ChunkHeader
    {
        public static uint ChunkHeaderSize { get => 8; }
        public string Tag { get; set; } = string.Empty;
        public uint ChunkSize { get; set; }

        public void ReadData(ByteChunk chunk)
        {
            Tag = Encoding.UTF8.GetString(chunk.ReadBytes(4));
            ChunkSize = chunk.ReadUInt32();
        }

        public static ChunkHeader PeekFromBytes(ByteChunk chunk)
        {
            var peakBytes = chunk.PeekChunk(8);
            var header = new ChunkHeader();
            header.ReadData(peakBytes);
            return header;
        }

        public static byte[] WriteData(ChunkHeader header)
        {
            if (header.Tag.Length != 4)
                throw new Exception($"Header not valid {header.Tag}");

            using var memStream = new MemoryStream();
            memStream.Write(ByteParsers.Byte.EncodeValue((byte)header.Tag[0], out _));
            memStream.Write(ByteParsers.Byte.EncodeValue((byte)header.Tag[1], out _));
            memStream.Write(ByteParsers.Byte.EncodeValue((byte)header.Tag[2], out _));
            memStream.Write(ByteParsers.Byte.EncodeValue((byte)header.Tag[3], out _));
            memStream.Write(ByteParsers.UInt32.EncodeValue(header.ChunkSize, out _));

            var byteArray = memStream.ToArray();

            // Reload the object to ensure sanity
            new ChunkHeader().ReadData(new ByteChunk(byteArray));
            return byteArray;
        }
    }
}
