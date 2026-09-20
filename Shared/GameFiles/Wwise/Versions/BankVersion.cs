using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Shared.GameFormats.Wwise.Versions
{
    public abstract class BankVersion
    {
        public abstract HircFactory HircFactory { get; }

        public abstract AkBkHircType DecodeHircType(byte hircType);
        public abstract byte EncodeHircType(AkBkHircType hircType);
        public abstract AkPluginType DecodePluginType(byte pluginType);
        public abstract AkRtpcType DecodeRtpcType(byte rtpcType);
        public abstract byte EncodeRtpcType(AkRtpcType rtpcType);
        public abstract AkPropId DecodePropertyId(byte propertyId);
        public abstract byte EncodePropertyId(AkPropId propertyId);

        public static AkGroupType DecodeGroupType(byte groupType)
        {
            return groupType switch
            {
                0x00 => AkGroupType.Switch,
                0x01 => AkGroupType.State,
                _ => AkGroupType.Unknown
            };
        }

        public static byte EncodeGroupType(AkGroupType groupType)
        {
            return groupType switch
            {
                AkGroupType.Switch => 0x00,
                AkGroupType.State => 0x01,
                _ => throw new NotSupportedException($"Unsupported group type: {groupType}.")
            };
        }

        public static AkMode DecodeDecisionTreeMode(byte decisionTreeMode)
        {
            return decisionTreeMode switch
            {
                0x00 => AkMode.BestMatch,
                0x01 => AkMode.Weighted,
                _ => AkMode.Unknown
            };
        }

        public static byte EncodeDecisionTreeMode(AkMode decisionTreeMode)
        {
            return decisionTreeMode switch
            {
                AkMode.BestMatch => 0x00,
                AkMode.Weighted => 0x01,
                _ => throw new NotSupportedException($"Unsupported decision-tree mode: {decisionTreeMode}.")
            };
        }

        public static AkTransitionMode DecodeTransitionMode(byte transitionMode)
        {
            return transitionMode switch
            {
                0x00 => AkTransitionMode.Disabled,
                0x01 => AkTransitionMode.CrossFadeAmp,
                0x02 => AkTransitionMode.CrossFadePower,
                0x03 => AkTransitionMode.Delay,
                0x04 => AkTransitionMode.SampleAccurate,
                0x05 => AkTransitionMode.TriggerRate,
                _ => AkTransitionMode.Unknown
            };
        }

        public static byte EncodeTransitionMode(AkTransitionMode transitionMode)
        {
            return transitionMode switch
            {
                AkTransitionMode.Disabled => 0x00,
                AkTransitionMode.CrossFadeAmp => 0x01,
                AkTransitionMode.CrossFadePower => 0x02,
                AkTransitionMode.Delay => 0x03,
                AkTransitionMode.SampleAccurate => 0x04,
                AkTransitionMode.TriggerRate => 0x05,
                _ => throw new NotSupportedException($"Unsupported transition mode: {transitionMode}.")
            };
        }

        public static AkRandomMode DecodeRandomMode(byte randomMode)
        {
            return randomMode switch
            {
                0x00 => AkRandomMode.Normal,
                0x01 => AkRandomMode.Shuffle,
                _ => AkRandomMode.Unknown
            };
        }

        public static byte EncodeRandomMode(AkRandomMode randomMode)
        {
            return randomMode switch
            {
                AkRandomMode.Normal => 0x00,
                AkRandomMode.Shuffle => 0x01,
                _ => throw new NotSupportedException($"Unsupported random mode: {randomMode}.")
            };
        }

        public static AkContainerMode DecodeContainerMode(byte containerMode)
        {
            return containerMode switch
            {
                0x00 => AkContainerMode.Random,
                0x01 => AkContainerMode.Sequence,
                _ => AkContainerMode.Unknown
            };
        }

        public static byte EncodeContainerMode(AkContainerMode containerMode)
        {
            return containerMode switch
            {
                AkContainerMode.Random => 0x00,
                AkContainerMode.Sequence => 0x01,
                _ => throw new NotSupportedException($"Unsupported container mode: {containerMode}.")
            };
        }
    }
}
