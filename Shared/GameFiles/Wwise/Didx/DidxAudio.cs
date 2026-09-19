namespace Shared.GameFormats.Wwise.Didx
{
    public class DidxAudio
    {
        public uint Id { get; set; }
        public required byte[] ByteArray { get; set; }
        public required string OwnerFilePath { get; set; }
        public uint LanguageId { get; set; }
    }
}
