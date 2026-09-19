namespace Shared.GameFormats.Wwise
{
    // Raw bank-generator versions emitted by the supported games. Their meaning and all format
    // selection belong to WwiseVersionDefinition; these constants remain the binary identities
    // used by fixtures, reports and bank headers.
    public static class BankVersion
    {
        // Warhammer III reports this exactly. The closest public Wwise release is 2019.2.15.7667,
        // whose generator version is 135; wwiser calls the CA variant 136.
        public const uint WarhammerIII = 2147483784;

        // Attila reports its version plainly.
        public const uint Attila = 112;
    }
}
