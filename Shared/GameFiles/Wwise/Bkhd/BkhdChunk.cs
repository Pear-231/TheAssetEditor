using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Bkhd
{
    public class BkhdChunk
    {
        public string OwnerFilePath { get; set; } = string.Empty;
        public ChunkHeader ChunkHeader { get; set; } = new ChunkHeader();
        public AkBankHeader AkBankHeader { get; set; } = new AkBankHeader();

        public void ReadData(string fileName, ByteChunk chunk)
        {
            OwnerFilePath = fileName;
            ChunkHeader.ReadData(chunk);
            AkBankHeader.ReadData(chunk, ChunkHeader.ChunkSize);
        }

        public static byte[] WriteData(BkhdChunk bkhdChunk)
        {
            using var memStream = new MemoryStream();
            memStream.Write(ChunkHeader.WriteData(bkhdChunk.ChunkHeader));
            memStream.Write(bkhdChunk.AkBankHeader.WriteData());
            var byteArray = memStream.ToArray();

            // Reload the object to ensure sanity
            var reload = new BkhdChunk();
            reload.ReadData("name", new ByteChunk(byteArray));

            return byteArray;
        }
    }
}
