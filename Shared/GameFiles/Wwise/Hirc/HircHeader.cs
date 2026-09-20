using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc
{
    public class HircHeader
    {
        public const uint Size = 9;
        public const uint PrefixSize = 5;

        public byte HircType { get; set; }
        public uint SectionSize { get; set; }
        public uint Id { get; set; }

        public void ReadData(ByteChunk chunk)
        {
            HircType = chunk.ReadByte();
            SectionSize = chunk.ReadUInt32();
            Id = chunk.ReadUInt32();
        }

        public static byte[] WriteData(HircHeader header)
        {
            using var memStream = new MemoryStream();
            memStream.Write(ByteParsers.Byte.EncodeValue(header.HircType, out _));
            memStream.Write(ByteParsers.UInt32.EncodeValue(header.SectionSize, out _));
            memStream.Write(ByteParsers.UInt32.EncodeValue(header.Id, out _));
            var byteArray = memStream.ToArray();

            // Reload the object to ensure sanity
            var sanityReload = new HircHeader();
            sanityReload.ReadData(new ByteChunk(byteArray));

            return byteArray;
        }
    }
}
