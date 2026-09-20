using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Shared.GameFormats.Wwise.Versions
{
    public sealed class BankVersion112 : BankVersion
    {
        private static readonly AkPropId[] s_propertyIds =
        [
            AkPropId.Volume,
            AkPropId.LFE,
            AkPropId.Pitch,
            AkPropId.LPF,
            AkPropId.HPF,
            AkPropId.BusVolume,
            AkPropId.Priority,
            AkPropId.PriorityDistanceOffset,
            AkPropId.FeedbackVolume,
            AkPropId.FeedbackLPF,
            AkPropId.MuteRatio,
            AkPropId.PAN_LR,
            AkPropId.PAN_FR,
            AkPropId.CenterPCT,
            AkPropId.DelayTime,
            AkPropId.TransitionTime,
            AkPropId.Probability,
            AkPropId.DialogueMode,
            AkPropId.UserAuxSendVolume0,
            AkPropId.UserAuxSendVolume1,
            AkPropId.UserAuxSendVolume2,
            AkPropId.UserAuxSendVolume3,
            AkPropId.GameAuxSendVolume,
            AkPropId.OutputBusVolume,
            AkPropId.OutputBusHPF,
            AkPropId.OutputBusLPF,
            AkPropId.HDRBusThreshold,
            AkPropId.HDRBusRatio,
            AkPropId.HDRBusReleaseTime,
            AkPropId.HDRBusGameParam,
            AkPropId.HDRBusGameParamMin,
            AkPropId.HDRBusGameParamMax,
            AkPropId.HDRActiveRange,
            AkPropId.MakeUpGain,
            AkPropId.LoopStart,
            AkPropId.LoopEnd,
            AkPropId.TrimInTime,
            AkPropId.TrimOutTime,
            AkPropId.FadeInTime,
            AkPropId.FadeOutTime,
            AkPropId.FadeInCurve,
            AkPropId.FadeOutCurve,
            AkPropId.LoopCrossfadeDuration,
            AkPropId.CrossfadeUpCurve,
            AkPropId.CrossfadeDownCurve,
            AkPropId.MidiTrackingRootNote,
            AkPropId.MidiPlayOnNoteType,
            AkPropId.MidiTransposition,
            AkPropId.MidiVelocityOffset,
            AkPropId.MidiKeyRangeMin,
            AkPropId.MidiKeyRangeMax,
            AkPropId.MidiVelocityRangeMin,
            AkPropId.MidiVelocityRangeMax,
            AkPropId.MidiChannelMask,
            AkPropId.PlaybackSpeed,
            AkPropId.MidiTempoSource,
            AkPropId.MidiTargetNode,
            AkPropId.AttachedPluginFXID,
            AkPropId.Loop,
            AkPropId.InitialDelay
        ];

        public override HircFactory HircFactory { get; } = HircFactory.CreateFactory_v112();

        public override AkBkHircType DecodeHircType(byte hircType)
        {
            return hircType switch
            {
                0x00 => AkBkHircType.None,
                0x01 => AkBkHircType.State,
                0x02 => AkBkHircType.Sound,
                0x03 => AkBkHircType.Action,
                0x04 => AkBkHircType.Event,
                0x05 => AkBkHircType.RandomSequenceContainer,
                0x06 => AkBkHircType.SwitchContainer,
                0x07 => AkBkHircType.ActorMixer,
                0x08 => AkBkHircType.Audio_Bus,
                0x09 => AkBkHircType.LayerContainer,
                0x0a => AkBkHircType.Music_Segment,
                0x0b => AkBkHircType.Music_Track,
                0x0c => AkBkHircType.Music_Switch,
                0x0d => AkBkHircType.Music_Random_Sequence,
                0x0e => AkBkHircType.Attenuation,
                0x0f => AkBkHircType.Dialogue_Event,
                0x10 => AkBkHircType.FeedbackBus,
                0x11 => AkBkHircType.FeedbackNode,
                0x12 => AkBkHircType.FxShareSet,
                0x13 => AkBkHircType.FxCustom,
                0x14 => AkBkHircType.AuxiliaryBus,
                0x15 => AkBkHircType.LFO,
                0x16 => AkBkHircType.Envelope,
                0x17 => AkBkHircType.AudioDevice,
                _ => AkBkHircType.Unknown
            };
        }

        public override byte EncodeHircType(AkBkHircType hircType)
        {
            return hircType switch
            {
                AkBkHircType.None => 0x00,
                AkBkHircType.State => 0x01,
                AkBkHircType.Sound => 0x02,
                AkBkHircType.Action => 0x03,
                AkBkHircType.Event => 0x04,
                AkBkHircType.RandomSequenceContainer => 0x05,
                AkBkHircType.SwitchContainer => 0x06,
                AkBkHircType.ActorMixer => 0x07,
                AkBkHircType.Audio_Bus => 0x08,
                AkBkHircType.LayerContainer => 0x09,
                AkBkHircType.Music_Segment => 0x0a,
                AkBkHircType.Music_Track => 0x0b,
                AkBkHircType.Music_Switch => 0x0c,
                AkBkHircType.Music_Random_Sequence => 0x0d,
                AkBkHircType.Attenuation => 0x0e,
                AkBkHircType.Dialogue_Event => 0x0f,
                AkBkHircType.FeedbackBus => 0x10,
                AkBkHircType.FeedbackNode => 0x11,
                AkBkHircType.FxShareSet => 0x12,
                AkBkHircType.FxCustom => 0x13,
                AkBkHircType.AuxiliaryBus => 0x14,
                AkBkHircType.LFO => 0x15,
                AkBkHircType.Envelope => 0x16,
                AkBkHircType.AudioDevice => 0x17,
                _ => throw new NotSupportedException($"Unsupported V112 HIRC type: {hircType}.")
            };
        }

        public override AkPluginType DecodePluginType(byte pluginType)
        {
            return pluginType switch
            {
                0x00 => AkPluginType.None,
                0x01 => AkPluginType.Codec,
                0x02 => AkPluginType.Source,
                0x03 => AkPluginType.Effect,
                0x04 => AkPluginType.MotionDevice,
                0x05 => AkPluginType.MotionSource,
                0x06 => AkPluginType.Mixer,
                0x07 => AkPluginType.Sink,
                _ => AkPluginType.Unknown
            };
        }

        public override AkRtpcType DecodeRtpcType(byte rtpcType)
        {
            return rtpcType switch
            {
                0x00 => AkRtpcType.GameParameter,
                0x01 => AkRtpcType.MIDIParameter,
                0x02 => AkRtpcType.Modulator,
                _ => AkRtpcType.Unknown
            };
        }

        public override byte EncodeRtpcType(AkRtpcType rtpcType)
        {
            return rtpcType switch
            {
                AkRtpcType.GameParameter => 0x00,
                AkRtpcType.MIDIParameter => 0x01,
                AkRtpcType.Modulator => 0x02,
                _ => throw new NotSupportedException($"Unsupported V112 RTPC type: {rtpcType}.")
            };
        }

        public override AkPropId DecodePropertyId(byte propertyId)
        {
            return propertyId < s_propertyIds.Length ? s_propertyIds[propertyId] : AkPropId.Unknown;
        }

        public override byte EncodePropertyId(AkPropId propertyId)
        {
            var rawId = Array.IndexOf(s_propertyIds, propertyId);
            return rawId >= 0 ? (byte)rawId : throw new NotSupportedException($"Unsupported V112 property ID: {propertyId}.");
        }
    }
}
