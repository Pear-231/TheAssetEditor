using Shared.GameFormats.Wwise.Versions.V112;
using Shared.GameFormats.Wwise.Versions.V136;

namespace Shared.GameFormats.Wwise.Versions
{
    public static class WwiseVersionResolver
    {
        public static WwiseVersionDefinition Resolve(uint rawBankGeneratorVersion)
        {
            return rawBankGeneratorVersion switch
            {
                BankVersion.Attila => WwiseV112Definition.Instance,
                BankVersion.WarhammerIII => WwiseCaV136Definition.Instance,
                _ => throw new NotSupportedException($"Unsupported Wwise bank generator version: {rawBankGeneratorVersion}.")
            };
        }
    }
}
