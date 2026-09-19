using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using HircV112 = Shared.GameFormats.Wwise.Hirc.V112;

namespace Shared.GameFormats.Wwise.Versions.V112
{
    public sealed class WwiseV112Definition : WwiseVersionDefinition
    {
        // Single source for both directions, transcribed from wwiser
        // wdefs.AkBank__AKBKHircType_126 and wparser.get_hirc_dispatch.
        private static readonly (byte RawType, AkBkHircType Type)[] s_hircTypes =
        [
            (0x10, AkBkHircType.FeedbackBus),
            (0x11, AkBkHircType.FeedbackNode),
            (0x12, AkBkHircType.FxShareSet),
            (0x13, AkBkHircType.FxCustom),
            (0x14, AkBkHircType.AuxiliaryBus),
            (0x15, AkBkHircType.LFO),
            (0x16, AkBkHircType.Envelope),
            (0x17, AkBkHircType.AudioDevice)
        ];

        public static WwiseV112Definition Instance { get; } = new WwiseV112Definition();

        private WwiseV112Definition()
            : base(BankVersion.Attila, 112, "V112", CreateHircFactory())
        {
        }

        public override bool UsesVariableActionExceptionCount => false;
        public override bool HasDangerousVirtualVoiceLimit => false;
        public override bool HasAcousticTextures => false;

        public override AkBkHircType DecodeHircType(byte rawType)
        {
            foreach (var mapping in s_hircTypes)
            {
                if (mapping.RawType == rawType)
                    return mapping.Type;
            }

            return (AkBkHircType)rawType;
        }

        public override byte EncodeHircType(AkBkHircType type)
        {
            foreach (var mapping in s_hircTypes)
            {
                if (mapping.Type == type)
                    return mapping.RawType;
            }

            return (byte)type;
        }

        public override AkActionParameter ClassifyAction(AkActionType actionType)
        {
            return ((ushort)actionType & 0xFF00) switch
            {
                // wwiser wparser_cls.CAkAction__Create, using the pre-128 action layouts.
                0x0400 or 0x0500 or 0x2300 => AkActionParameter.Play,
                0x0100 or 0x2200 => AkActionParameter.Active,
                0x0200 or 0x0300 => AkActionParameter.ActiveWithSpecificParameter,
                0x0600 or 0x0700 => AkActionParameter.Mute,
                0x0800 or 0x0900 or 0x0A00 or 0x0B00 or 0x0C00 or 0x0D00 or 0x0E00 or 0x0F00 or 0x2000 or 0x3000 => AkActionParameter.SetProperty,
                0x1300 or 0x1400 => AkActionParameter.SetGameParameter,
                0x1A00 or 0x1B00 or 0x3300 or 0x3400 or 0x3500 or 0x3600 or 0x3700 => AkActionParameter.BypassEffects,
                0x1E00 => AkActionParameter.Seek,
                _ => AkActionParameter.None
            };
        }

        public override bool TryMapProperty(byte rawPropertyId, out WwiseProperty property)
        {
            // wwiser wdefs.AkPropID_113, selected for bank version 112.
            switch (rawPropertyId)
            {
                case 0x00: property = WwiseProperty.Volume; return true;
                case 0x05: property = WwiseProperty.BusVolume; return true;
                case 0x21: property = WwiseProperty.MakeUpGain; return true;
                case 0x17: property = WwiseProperty.OutputBusVolume; return true;
                case 0x02: property = WwiseProperty.Pitch; return true;
                case 0x03: property = WwiseProperty.LowPassFilter; return true;
                case 0x04: property = WwiseProperty.HighPassFilter; return true;
                case 0x06: property = WwiseProperty.Priority; return true;
                case 0x07: property = WwiseProperty.PriorityDistanceOffset; return true;
                case 0x3B: property = WwiseProperty.InitialDelay; return true;
                case 0x0E: property = WwiseProperty.ActionDelay; return true;
                case 0x0F: property = WwiseProperty.TransitionTime; return true;
                case 0x10: property = WwiseProperty.Probability; return true;
                case 0x3A: property = WwiseProperty.LoopCount; return true;
                case 0x0D: property = WwiseProperty.CentrePercent; return true;
                case 0x0B: property = WwiseProperty.PanLeftRight; return true;
                case 0x0C: property = WwiseProperty.PanFrontRear; return true;
                case 0x16: property = WwiseProperty.GameAuxSendVolume; return true;
                case 0x12: property = WwiseProperty.UserAuxSendVolume0; return true;
                case 0x13: property = WwiseProperty.UserAuxSendVolume1; return true;
                case 0x14: property = WwiseProperty.UserAuxSendVolume2; return true;
                case 0x15: property = WwiseProperty.UserAuxSendVolume3; return true;
                default: property = default; return false;
            }
        }

        private static HircFactory CreateHircFactory()
        {
            var factory = new HircFactory();
            factory.RegisterHirc(AkBkHircType.ActorMixer, () => new HircV112.CAkActorMixer_V112());
            factory.RegisterHirc(AkBkHircType.State, () => new HircV112.CAkState_V112());
            factory.RegisterHirc(AkBkHircType.Sound, () => new HircV112.CAkSound_V112());
            factory.RegisterHirc(AkBkHircType.Event, () => new HircV112.CAkEvent_V112());
            factory.RegisterHirc(AkBkHircType.Action, () => new HircV112.CAkAction_V112());
            factory.RegisterHirc(AkBkHircType.SwitchContainer, () => new HircV112.CAkSwitchCntr_V112());
            factory.RegisterHirc(AkBkHircType.RandomSequenceContainer, () => new HircV112.CAkRanSeqCntr_V112());
            factory.RegisterHirc(AkBkHircType.LayerContainer, () => new HircV112.CAkLayerCntr_V112());
            factory.RegisterHirc(AkBkHircType.Dialogue_Event, () => new HircV112.CAkDialogueEvent_V112());
            factory.RegisterHirc(AkBkHircType.Music_Track, () => new HircV112.CAkMusicTrack_V112());
            factory.RegisterHirc(AkBkHircType.Music_Segment, () => new HircV112.CAkMusicSegment_V112());
            factory.RegisterHirc(AkBkHircType.Music_Random_Sequence, () => new HircV112.CAkMusicRanSeqCntr_V112());
            factory.RegisterHirc(AkBkHircType.Music_Switch, () => new HircV112.CAkMusicSwitchCntr_V112());
            factory.RegisterHirc(AkBkHircType.FxCustom, () => new HircV112.CAkFxCustom_V112());
            factory.RegisterHirc(AkBkHircType.FxShareSet, () => new HircV112.CAkFxShareSet_V112());
            factory.RegisterHirc(AkBkHircType.Attenuation, () => new HircV112.CAkAttenuation_V112());
            factory.RegisterHirc(AkBkHircType.Audio_Bus, () => new HircV112.CAkBus_V112());
            factory.RegisterHirc(AkBkHircType.AuxiliaryBus, () => new HircV112.CAkBus_V112());
            return factory;
        }
    }
}
