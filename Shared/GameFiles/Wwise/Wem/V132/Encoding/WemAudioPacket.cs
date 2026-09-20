using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Wem.V132.Encoding
{
    public class WemAudioPacket
    {
        public const int LengthPrefixSize = sizeof(ushort);

        public byte[] Data { get; set; } = [];
        public long GranulePosition { get; set; }

        public void ReadData(ByteChunk chunk)
        {
            var dataSize = chunk.ReadUShort();
            Data = chunk.ReadBytes(dataSize);
        }

        public byte[] WriteData()
        {
            using var stream = new MemoryStream();
            stream.Write(ByteParsers.UShort.EncodeValue((ushort)Data.Length, out _));
            stream.Write(Data);
            return stream.ToArray();
        }
    }
}
