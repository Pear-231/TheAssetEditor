using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc
{
    using Shared.GameFormats.Wwise.Versions;

    public class HircChunk
    {
        public static uint ChunkHeaderSize { get => 4; }
        public ChunkHeader ChunkHeader { get; set; } = new ChunkHeader();
        public uint NumHircItems { get; set; }
        public List<HircItem> HircItems { get; set; } = [];

        public void ReadData(string filePath, ByteChunk chunk, BankVersion bankVersion, uint languageId, bool isCA)
        {
            ChunkHeader.ReadData(chunk);
            NumHircItems = chunk.ReadUInt32();

            for (uint itemIndex = 0; itemIndex < NumHircItems; itemIndex++)
                HircItems.Add(bankVersion.HircFactory.ReadHirc(filePath, chunk, bankVersion, languageId, isCA, itemIndex));

            var expectedChunkSize = ChunkHeaderSize + HircItems.Sum(hirc => HircHeader.PrefixSize + hirc.SectionSize);
            if (expectedChunkSize != ChunkHeader.ChunkSize)
                throw new Exception("Error parsing HIRC in bnk, expected and actual not matching");
        }

        public static List<HircIndexEntry> BuildIndex(long payloadOffset, uint chunkSize, ByteChunk chunk, BankVersion bankVersion)
        {
            if (chunkSize < sizeof(uint))
                throw new InvalidDataException($"HIRC chunk is only {chunkSize} bytes.");

            var result = new List<HircIndexEntry>();
            var hircCount = chunk.ReadUInt32();

            for (uint itemIndex = 0; itemIndex < hircCount; itemIndex++)
            {
                if (chunk.BytesLeft < HircHeader.Size)
                    throw new InvalidDataException($"HIRC item {itemIndex} does not contain a complete header.");

                var itemOffsetInChunk = chunk.Index;
                var header = new HircHeader();
                header.ReadData(chunk);
                var hircType = bankVersion.DecodeHircType(header.HircType);
                if (header.SectionSize < sizeof(uint))
                    throw new InvalidDataException($"HIRC item {itemIndex} has an invalid section size of {header.SectionSize}.");

                var hircLength = checked(HircHeader.PrefixSize + header.SectionSize);
                if (hircLength > int.MaxValue || hircLength - HircHeader.Size > chunk.BytesLeft)
                    throw new InvalidDataException($"HIRC item {itemIndex} extends beyond its HIRC chunk.");

                result.Add(
                    new HircIndexEntry
                    {
                        HircType = hircType,
                        Header = header,
                        Offset = payloadOffset + itemOffsetInChunk,
                        Length = (int)hircLength,
                        Index = itemIndex
                    });
                chunk.Advance((int)(hircLength - HircHeader.Size));
            }

            if (chunk.BytesLeft != 0)
                throw new InvalidDataException($"HIRC index left {chunk.BytesLeft} unread bytes in the chunk.");

            return result;
        }

        public static byte[] WriteData(HircChunk hircChunk, uint gameBankGeneratorVersion)
        {
            var bankVersion = BankVersionResolver.Resolve(gameBankGeneratorVersion);
            using var memStream = new MemoryStream();
            memStream.Write(ChunkHeader.WriteData(hircChunk.ChunkHeader));
            memStream.Write(ByteParsers.UInt32.EncodeValue(hircChunk.NumHircItems, out _));

            foreach (var hircItem in hircChunk.HircItems)
            {
                hircItem.Header.HircType = bankVersion.EncodeHircType(hircItem.HircType);
                var bytes = hircItem.WriteData(bankVersion);
                memStream.Write(bytes);
            }

            var byteArray = memStream.ToArray();

            // Reload to ensure sanity
            var sanityReload = new HircChunk();
            sanityReload.ReadData("name", new ByteChunk(byteArray), bankVersion, 0, true);

            return byteArray;
        }
    }
}
