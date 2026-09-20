using Shared.Core.Settings;

namespace Shared.GameFormats.Wwise.Versions
{
    public static class BankVersionResolver
    {
        private static readonly BankVersion s_bankVersion112 = new BankVersion112();
        private static readonly BankVersion s_bankVersion136 = new BankVersion136();

        public static BankVersion Resolve(uint bankGeneratorVersion)
        {
            return bankGeneratorVersion switch
            {
                (uint)GameBnkVersion.Attila => s_bankVersion112,
                (uint)GameBnkVersion.Warhammer3 => s_bankVersion136,
                _ => throw new NotSupportedException($"Unsupported bank generator version: {bankGeneratorVersion}.")
            };
        }
    }
}
