using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using HircV136 = Shared.GameFormats.Wwise.Hirc.V136;

namespace Shared.GameFormats.Wwise.Versions.V136
{
    public sealed class WwiseCaV136Definition : WwiseVersionDefinition
    {
        public static WwiseCaV136Definition Instance { get; } = new WwiseCaV136Definition();

        private WwiseCaV136Definition()
            : base(BankVersion.WarhammerIII, 136, "CA-pseudo-V136", CreateHircFactory())
        {
        }

        public override bool UsesVariableActionExceptionCount => true;
        public override bool HasDangerousVirtualVoiceLimit => true;
        public override bool HasAcousticTextures => true;

        // wwiser wdefs.AkBank__AKBKHircType_128 and wparser.get_hirc_dispatch. CA's raw
        // 0x80000088 generator version selects the ordinary 136-era schema.
        public override AkBkHircType DecodeHircType(byte rawType) => (AkBkHircType)rawType;
        public override byte EncodeHircType(AkBkHircType type) => (byte)type;

        public override AkActionParameter ClassifyAction(AkActionType actionType)
        {
            return ((ushort)actionType & 0xFF00) switch
            {
                // wwiser wparser_cls.CAkAction__Create, using the 128-and-later layouts.
                0x0400 or 0x0500 or 0x2300 => AkActionParameter.Play,
                0x0100 or 0x0200 or 0x0300 => AkActionParameter.ActiveWithSpecificParameter,
                0x2200 => AkActionParameter.Active,
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
            // wwiser wdefs.AkPropID_128, selected for schema version 136.
            switch (rawPropertyId)
            {
                case 0x00: property = WwiseProperty.Volume; return true;
                case 0x05: property = WwiseProperty.BusVolume; return true;
                case 0x06: property = WwiseProperty.MakeUpGain; return true;
                case 0x18: property = WwiseProperty.OutputBusVolume; return true;
                case 0x02: property = WwiseProperty.Pitch; return true;
                case 0x03: property = WwiseProperty.LowPassFilter; return true;
                case 0x04: property = WwiseProperty.HighPassFilter; return true;
                case 0x07: property = WwiseProperty.Priority; return true;
                case 0x08: property = WwiseProperty.PriorityDistanceOffset; return true;
                case 0x3B: property = WwiseProperty.InitialDelay; return true;
                case 0x0F: property = WwiseProperty.ActionDelay; return true;
                case 0x10: property = WwiseProperty.TransitionTime; return true;
                case 0x11: property = WwiseProperty.Probability; return true;
                case 0x3A: property = WwiseProperty.LoopCount; return true;
                case 0x0E: property = WwiseProperty.CentrePercent; return true;
                case 0x0C: property = WwiseProperty.PanLeftRight; return true;
                case 0x0D: property = WwiseProperty.PanFrontRear; return true;
                case 0x17: property = WwiseProperty.GameAuxSendVolume; return true;
                case 0x13: property = WwiseProperty.UserAuxSendVolume0; return true;
                case 0x14: property = WwiseProperty.UserAuxSendVolume1; return true;
                case 0x15: property = WwiseProperty.UserAuxSendVolume2; return true;
                case 0x16: property = WwiseProperty.UserAuxSendVolume3; return true;
                case 0x46: property = WwiseProperty.AttenuationId; return true;
                default: property = default; return false;
            }
        }

        private static HircFactory CreateHircFactory()
        {
            var factory = new HircFactory();
            factory.RegisterHirc(AkBkHircType.ActorMixer, () => new HircV136.CAkActorMixer_V136());
            factory.RegisterHirc(AkBkHircType.State, () => new HircV136.CAkState_V136());
            factory.RegisterHirc(AkBkHircType.Sound, () => new HircV136.CAkSound_V136());
            factory.RegisterHirc(AkBkHircType.Event, () => new HircV136.CAkEvent_V136());
            factory.RegisterHirc(AkBkHircType.Action, () => new HircV136.CAkAction_V136());
            factory.RegisterHirc(AkBkHircType.SwitchContainer, () => new HircV136.CAkSwitchCntr_V136());
            factory.RegisterHirc(AkBkHircType.RandomSequenceContainer, () => new HircV136.CAkRanSeqCntr_V136());
            factory.RegisterHirc(AkBkHircType.LayerContainer, () => new HircV136.CAkLayerCntr_V136());
            factory.RegisterHirc(AkBkHircType.Dialogue_Event, () => new HircV136.CAkDialogueEvent_V136());
            factory.RegisterHirc(AkBkHircType.Music_Track, () => new HircV136.CAkMusicTrack_V136());
            factory.RegisterHirc(AkBkHircType.Music_Segment, () => new HircV136.CAkMusicSegment_V136());
            factory.RegisterHirc(AkBkHircType.Music_Random_Sequence, () => new HircV136.CAkMusicRanSeqCntr_V136());
            factory.RegisterHirc(AkBkHircType.Music_Switch, () => new HircV136.CAkMusicSwitchCntr_V136());
            factory.RegisterHirc(AkBkHircType.FxCustom, () => new HircV136.CAkFxCustom_V136());
            factory.RegisterHirc(AkBkHircType.FxShareSet, () => new HircV136.CAkFxShareSet_V136());
            factory.RegisterHirc(AkBkHircType.Audio_Bus, () => new HircV136.CAkBus_V136());
            factory.RegisterHirc(AkBkHircType.AuxiliaryBus, () => new HircV136.CAkBus_V136());
            factory.RegisterHirc(AkBkHircType.Attenuation, () => new HircV136.CAkAttenuation_V136());
            factory.RegisterHirc(AkBkHircType.LFO, () => new HircV136.CAkLfoModulator_V136());
            factory.RegisterHirc(AkBkHircType.Envelope, () => new HircV136.CAkEnvelopeModulator_V136());
            factory.RegisterHirc(AkBkHircType.AudioDevice, () => new HircV136.CAkAudioDevice_V136());
            factory.RegisterHirc(AkBkHircType.TimeMod, () => new HircV136.CAkTimeModulator_V136());
            return factory;
        }
    }
}
