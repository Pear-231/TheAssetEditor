namespace Shared.GameFormats.Wwise
{
    // Each major release of Wwise has a bank generator version
    // CA sometimes use an in-house compiled version of Wwise which is based on a public release with custom modifications to some Wwise objects
    // The bank generator version of the closest public release (2019.2.15.7667) to that used in Wh3 (2147483784) is 135
    // Wwiser adds 1 to that for internal use to create a pseudo version called 136 but really it's 2147483784
    //
    // Their meaning and all format selection belong to WwiseVersionDefinition; these constants
    // remain the binary identities used by fixtures, reports and bank headers.
    public static class BankVersion
    {
        public const uint WarhammerIII = 2147483784;

        // Attila reports its version plainly.
        public const uint Attila = 112;
    }
}
