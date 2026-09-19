using System.Text;
using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Plat
{
    // The PLAT chunk: the name of the custom platform the bank was built for.
    //
    // Bank version 113 and later only, which is why a V112 init bank correctly has none. From 137
    // the string becomes null-terminated rather than length-prefixed; this repo reads neither
    // version above 136, so only the length-prefixed form is handled.
    public class PlatChunk
    {
        public ChunkHeader ChunkHeader { get; set; } = new ChunkHeader();
        public string CustomPlatformName { get; set; } = string.Empty;

        public void ReadData(string fileName, ByteChunk chunk)
        {
            ChunkHeader.ReadData(chunk);
            var stringSize = chunk.ReadUInt32();
            CustomPlatformName = Encoding.UTF8.GetString(chunk.ReadBytes((int)stringSize)).TrimEnd('\0');
        }
    }
}
