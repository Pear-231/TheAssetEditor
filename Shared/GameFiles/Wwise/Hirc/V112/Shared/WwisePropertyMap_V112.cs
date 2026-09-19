using static Shared.GameFormats.Wwise.Enums.Enums_V112;

using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc.V112.Shared
{
    // V112 numbers its properties differently from V136 -- priority is 0x06 here and 0x07 there --
    // so the mapping onto the version-agnostic set is per version and cannot be shared. V112 has no
    // attenuation property: it carries that reference in its positioning parameters instead.
    internal static class WwisePropertyMap_V112
    {
        private static readonly WwiseVersionDefinition s_versionDefinition = WwiseVersionResolver.Resolve(BankVersion.Attila);

        public static AuthoredProperties Read(AkPropBundle_V112 values, AkPropBundleMinMax_V112 ranges)
        {
            var mappedValues = new List<AuthoredPropertyValue>(values.PropsList.Count);
            foreach (var property in values.PropsList)
            {
                if (TryMap(property.Id, out var mapped))
                    mappedValues.Add(new AuthoredPropertyValue(mapped, property.Value));
            }

            var mappedRanges = new List<(AuthoredPropertyValue, AuthoredPropertyValue)>(ranges.PropsList.Count);
            foreach (var property in ranges.PropsList)
            {
                if (TryMap(property.Type, out var mapped))
                    mappedRanges.Add((new AuthoredPropertyValue(mapped, property.Min), new AuthoredPropertyValue(mapped, property.Max)));
            }

            return new AuthoredProperties(mappedValues, mappedRanges);
        }

        private static bool TryMap(AkPropId_V112 id, out WwiseProperty property)
            => s_versionDefinition.TryMapProperty((byte)id, out property);
    }
}
