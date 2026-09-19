namespace Shared.GameFormats.Wwise
{
    // What a bank's stated generator version means when it has to be compared rather than matched.
    //
    // CA build Wwise in-house, and Warhammer III's banks report 2147483784 rather than a small
    // number: the high bit is set and the low bits are the generator version. Matching that exact
    // value is enough where a reader only has to pick between two shapes, which is why HircFactory
    // switches on it -- but a chunk whose layout has several version gates has to ask whether the
    // version is above or below each one, and for that the number has to be a number.
    public static class BankVersion
    {
        // Warhammer III reports this exactly. The closest public Wwise release is 2019.2.15.7667,
        // whose generator version is 135; wwiser calls the CA variant 136.
        public const uint WarhammerIII = 2147483784;

        // Attila reports its version plainly.
        public const uint Attila = 112;

        private const uint HighBit = 0x80000000;

        public static uint Normalise(uint bankGeneratorVersion)
            => (bankGeneratorVersion & HighBit) == 0 ? bankGeneratorVersion : bankGeneratorVersion & 0x7FFFFFFF;
    }
}
