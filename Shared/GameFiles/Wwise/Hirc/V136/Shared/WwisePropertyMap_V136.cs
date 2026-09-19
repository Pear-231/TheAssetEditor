using Shared.GameFormats.Wwise.Enums;

using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc.V136.Shared
{
    // V136's property numbering, mapped onto the version-agnostic set the engine reads. Properties
    // with no entry here are still parsed and kept; naming them would claim an understanding of what
    // they mean that this project has not established.
    internal static class WwisePropertyMap_V136
    {
        private static readonly WwiseVersionDefinition s_versionDefinition = WwiseVersionResolver.Resolve(BankVersion.WarhammerIII);

        public static AuthoredProperties Read(AkPropBundle_V136 values, AkPropBundleMinMax_V136 ranges)
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

        private static bool TryMap(AkPropId_V136 id, out WwiseProperty property)
            => s_versionDefinition.TryMapProperty((byte)id, out property);
    }
}
