using Shared.GameFormats.Wwise.Enums;

namespace Shared.GameFormats.Wwise.Hirc.V136.Shared
{
    // V136's property numbering, mapped onto the version-agnostic set the engine reads. Properties
    // with no entry here are still parsed and kept; naming them would claim an understanding of what
    // they mean that this project has not established.
    internal static class WwisePropertyMap_V136
    {
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
        {
            switch (id)
            {
                case AkPropId_V136.Volume: property = WwiseProperty.Volume; return true;
                case AkPropId_V136.BusVolume: property = WwiseProperty.BusVolume; return true;
                case AkPropId_V136.MakeUpGain: property = WwiseProperty.MakeUpGain; return true;
                case AkPropId_V136.OutputBusVolume: property = WwiseProperty.OutputBusVolume; return true;
                case AkPropId_V136.Pitch: property = WwiseProperty.Pitch; return true;
                case AkPropId_V136.LPF: property = WwiseProperty.LowPassFilter; return true;
                case AkPropId_V136.HPF: property = WwiseProperty.HighPassFilter; return true;
                case AkPropId_V136.Priority: property = WwiseProperty.Priority; return true;
                case AkPropId_V136.PriorityDistanceOffset: property = WwiseProperty.PriorityDistanceOffset; return true;
                case AkPropId_V136.InitialDelay: property = WwiseProperty.InitialDelay; return true;
                case AkPropId_V136.DelayTime: property = WwiseProperty.ActionDelay; return true;
                case AkPropId_V136.TransitionTime: property = WwiseProperty.TransitionTime; return true;
                case AkPropId_V136.Probability: property = WwiseProperty.Probability; return true;
                case AkPropId_V136.Loop: property = WwiseProperty.LoopCount; return true;
                case AkPropId_V136.CenterPCT: property = WwiseProperty.CentrePercent; return true;
                case AkPropId_V136.PAN_LR: property = WwiseProperty.PanLeftRight; return true;
                case AkPropId_V136.PAN_FR: property = WwiseProperty.PanFrontRear; return true;
                case AkPropId_V136.GameAuxSendVolume: property = WwiseProperty.GameAuxSendVolume; return true;
                case AkPropId_V136.UserAuxSendVolume0: property = WwiseProperty.UserAuxSendVolume0; return true;
                case AkPropId_V136.UserAuxSendVolume1: property = WwiseProperty.UserAuxSendVolume1; return true;
                case AkPropId_V136.UserAuxSendVolume2: property = WwiseProperty.UserAuxSendVolume2; return true;
                case AkPropId_V136.UserAuxSendVolume3: property = WwiseProperty.UserAuxSendVolume3; return true;
                case AkPropId_V136.AttenuationID: property = WwiseProperty.AttenuationId; return true;
                default: property = default; return false;
            }
        }
    }
}
