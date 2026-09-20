using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Bkhd;
using Shared.GameFormats.Wwise.Data;
using Shared.GameFormats.Wwise.Didx;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Stid;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise
{
    public class BnkFile
    {
        public BkhdChunk BkhdChunk { get; set; } = new BkhdChunk();
        public HircChunk? HircChunk { get; set; }
        public DidxChunk? DidxChunk { get; set; }
        public DataChunk? DataChunk { get; set; }
        public StidChunk? StidChunk { get; set; }

        public static BnkFile CreateFromBytes(byte[] bnkBytes, string filePath, bool isCA)
        {
            var bnkFile = new BnkFile();
            bnkFile.ReadData(new ByteChunk(bnkBytes), filePath, isCA);
            return bnkFile;
        }

        public void ReadData(ByteChunk chunk, string filePath, bool isCA)
        {
            BankVersion? bankVersion = null;

            while (chunk.BytesLeft != 0)
            {
                var chunkHeader = ChunkHeader.PeekFromBytes(chunk);
                var indexBeforeRead = chunk.Index;
                var expectedIndexAfterRead = indexBeforeRead + ChunkHeader.ChunkHeaderSize + chunkHeader.ChunkSize;

                if (BankChunkTypes.BKHD == chunkHeader.Tag)
                {
                    BkhdChunk.ReadData(filePath, chunk);
                    bankVersion = BankVersionResolver.Resolve(BkhdChunk.AkBankHeader.BankGeneratorVersion);
                }
                else if (BankChunkTypes.HIRC == chunkHeader.Tag)
                {
                    if (bankVersion is null)
                        throw new InvalidDataException($"Unable to determine version of {filePath}");

                    HircChunk = new HircChunk();
                    HircChunk.ReadData(filePath, chunk, bankVersion, BkhdChunk.AkBankHeader.LanguageId, isCA);
                }
                else if (BankChunkTypes.DIDX == chunkHeader.Tag)
                {
                    DidxChunk = new DidxChunk();
                    DidxChunk.ReadData(filePath, chunk);
                }
                else if (BankChunkTypes.DATA == chunkHeader.Tag)
                {
                    DataChunk = new DataChunk();
                    DataChunk.ReadData(filePath, chunk);
                }
                else if (BankChunkTypes.STID == chunkHeader.Tag)
                {
                    StidChunk = new StidChunk();
                    StidChunk.ReadData(filePath, chunk);
                }
                else
                    throw new ArgumentException($"Unknown data block '{chunkHeader.Tag}' while parsing bnk file '{filePath}'");

                // Verify
                var bytesRead = expectedIndexAfterRead - indexBeforeRead;
                if (chunk.Index != expectedIndexAfterRead)
                    throw new Exception($"Error parsing bnk with tag '{chunkHeader.Tag}', incorrect num bytes read. '{bytesRead}' bytes read in this operation");
            }

            if (chunk.BytesLeft != 0)
                throw new Exception("Error parsing bnk, bytes left");
        }

        public static BnkIndex BuildIndex(string filePath, long decodedSize, Func<long, int, byte[]> readData)
        {
            var result = new BnkIndex();
            BankVersion? bankVersion = null;
            long chunkOffset = 0;

            while (chunkOffset < decodedSize)
            {
                if (decodedSize - chunkOffset < ChunkHeader.ChunkHeaderSize)
                    throw new InvalidDataException($"BNK chunk header extends beyond the end of '{filePath}'.");

                var chunkHeader = new ChunkHeader();
                chunkHeader.ReadData(new ByteChunk(readData(chunkOffset, checked((int)ChunkHeader.ChunkHeaderSize))));
                var payloadOffset = checked(chunkOffset + ChunkHeader.ChunkHeaderSize);
                var nextChunkOffset = checked(payloadOffset + chunkHeader.ChunkSize);
                if (nextChunkOffset > decodedSize)
                    throw new InvalidDataException($"BNK chunk '{chunkHeader.Tag}' extends beyond the end of '{filePath}'.");

                if (chunkHeader.Tag == BankChunkTypes.BKHD)
                {
                    var totalChunkSize = checked((int)(ChunkHeader.ChunkHeaderSize + chunkHeader.ChunkSize));
                    var bkhdChunk = new BkhdChunk();
                    bkhdChunk.ReadData(filePath, new ByteChunk(readData(chunkOffset, totalChunkSize)));
                    result.BankGeneratorVersion = bkhdChunk.AkBankHeader.BankGeneratorVersion;
                    bankVersion = BankVersionResolver.Resolve(result.BankGeneratorVersion);
                    result.LanguageId = bkhdChunk.AkBankHeader.LanguageId;
                }
                else if (chunkHeader.Tag == BankChunkTypes.HIRC)
                {
                    if (chunkHeader.ChunkSize > int.MaxValue)
                        throw new InvalidDataException($"HIRC chunk is too large to index: {chunkHeader.ChunkSize} bytes.");

                    if (bankVersion is null)
                        throw new InvalidDataException($"Wwise bank '{filePath}' contains versioned data before its BKHD chunk.");

                    var hircEntries = HircChunk.BuildIndex(payloadOffset, chunkHeader.ChunkSize, new ByteChunk(readData(payloadOffset, (int)chunkHeader.ChunkSize)), bankVersion);
                    result.HircEntries.AddRange(hircEntries);
                }
                else if (chunkHeader.Tag == BankChunkTypes.DIDX)
                {
                    if (chunkHeader.ChunkSize > int.MaxValue)
                        throw new InvalidDataException($"DIDX chunk is too large to index: {chunkHeader.ChunkSize} bytes.");

                    var mediaHeaders = DidxChunk.ReadMediaHeaders(new ByteChunk(readData(payloadOffset, (int)chunkHeader.ChunkSize)), chunkHeader.ChunkSize);
                    result.DidxEntries.AddRange(mediaHeaders);
                }
                else if (chunkHeader.Tag == BankChunkTypes.DATA)
                    result.DataOffset = payloadOffset;

                chunkOffset = nextChunkOffset;
            }

            return result;
        }
    }
}
