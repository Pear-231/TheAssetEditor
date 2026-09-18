using static Shared.GameFormats.Wwise.Enums.Enums_V112;

namespace Shared.GameFormats.Wwise.Hirc.V112.Shared
{
    // V112 numbers its properties differently from V136 -- priority is 0x06 here and 0x07 there --
    // so the mapping onto the version-agnostic set is per version and cannot be shared. V112 has no
    // attenuation property: it carries that reference in its positioning parameters instead.
    internal static class WwisePropertyMap_V112
    {
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
        {
            switch (id)
            {
                case AkPropId_V112.Volume: property = WwiseProperty.Volume; return true;
                case AkPropId_V112.BusVolume: property = WwiseProperty.BusVolume; return true;
                case AkPropId_V112.MakeUpGain: property = WwiseProperty.MakeUpGain; return true;
                case AkPropId_V112.OutputBusVolume: property = WwiseProperty.OutputBusVolume; return true;
                case AkPropId_V112.Pitch: property = WwiseProperty.Pitch; return true;
                case AkPropId_V112.LPF: property = WwiseProperty.LowPassFilter; return true;
                case AkPropId_V112.HPF: property = WwiseProperty.HighPassFilter; return true;
                case AkPropId_V112.Priority: property = WwiseProperty.Priority; return true;
                case AkPropId_V112.PriorityDistanceOffset: property = WwiseProperty.PriorityDistanceOffset; return true;
                case AkPropId_V112.InitialDelay: property = WwiseProperty.InitialDelay; return true;
                case AkPropId_V112.DelayTime: property = WwiseProperty.ActionDelay; return true;
                case AkPropId_V112.TransitionTime: property = WwiseProperty.TransitionTime; return true;
                case AkPropId_V112.Probability: property = WwiseProperty.Probability; return true;
                case AkPropId_V112.Loop: property = WwiseProperty.LoopCount; return true;
                case AkPropId_V112.CenterPCT: property = WwiseProperty.CentrePercent; return true;
                case AkPropId_V112.PAN_LR: property = WwiseProperty.PanLeftRight; return true;
                case AkPropId_V112.PAN_FR: property = WwiseProperty.PanFrontRear; return true;
                case AkPropId_V112.GameAuxSendVolume: property = WwiseProperty.GameAuxSendVolume; return true;
                case AkPropId_V112.UserAuxSendVolume0: property = WwiseProperty.UserAuxSendVolume0; return true;
                case AkPropId_V112.UserAuxSendVolume1: property = WwiseProperty.UserAuxSendVolume1; return true;
                case AkPropId_V112.UserAuxSendVolume2: property = WwiseProperty.UserAuxSendVolume2; return true;
                case AkPropId_V112.UserAuxSendVolume3: property = WwiseProperty.UserAuxSendVolume3; return true;
                default: property = default; return false;
            }
        }
    }
}
