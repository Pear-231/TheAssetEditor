using Shared.ByteParsing;

namespace Shared.GameFormats.Db
{
    public class DbTableHeader
    {
        private static readonly byte[] s_guidMarker = [253, 254, 252, 255];
        private static readonly byte[] s_versionMarker = [252, 253, 254, 255];

        public int Version { get; set; }
        public bool MysteriousByte { get; set; }
        public string Guid { get; set; } = string.Empty;
        public uint EntryCount { get; set; }

        public static DbTableHeader ReadData(ByteChunk chunk)
        {
            if (chunk.BytesLeft < 5)
                throw new InvalidDataException("Data is too small to be a Db table.");

            var guid = string.Empty;
            var potentialGuidMarker = chunk.ReadBytes(4);
            if (potentialGuidMarker.SequenceEqual(s_guidMarker))
                guid = chunk.ReadStringAscii();
            else
                chunk.Index -= 4;

            var version = 0;
            var potentialVersionMarker = chunk.ReadBytes(4);
            if (potentialVersionMarker.SequenceEqual(s_versionMarker))
                version = chunk.ReadInt32();
            else
                chunk.Index -= 4;

            return new DbTableHeader
            {
                Version = version,
                Guid = guid,
                MysteriousByte = chunk.ReadBool(),
                EntryCount = chunk.ReadUInt32()
            };
        }
    }
}
