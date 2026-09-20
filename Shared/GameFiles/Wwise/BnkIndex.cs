using Shared.GameFormats.Wwise.Didx;
using Shared.GameFormats.Wwise.Hirc;

namespace Shared.GameFormats.Wwise
{
    public class BnkIndex
    {
        public uint BankGeneratorVersion { get; set; }
        public uint LanguageId { get; set; }
        public long? DataOffset { get; set; }
        public List<HircIndexEntry> HircEntries { get; set; } = [];
        public List<MediaHeader> DidxEntries { get; set; } = [];
    }
}
