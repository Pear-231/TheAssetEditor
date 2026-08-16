using Editors.Audio.Shared.Wwise.Engine.Mixing;

namespace Editors.Audio.Shared.Wwise.Engine.Scheduling
{
    internal sealed class ScheduledVoice
    {
        public bool IsActive => PcmData != null;
        public long VoiceIdentifier { get; private set; }
        public long PlaybackGeneration { get; private set; }
        public byte[] PcmData { get; private set; }
        public long TimelineStartFrame { get; private set; }
        public bool IsLooping { get; private set; }

        public void ApplyScheduleCommand(MixerCommand mixerCommand)
        {
            VoiceIdentifier = mixerCommand.VoiceIdentifier;
            PlaybackGeneration = mixerCommand.PlaybackGeneration;
            PcmData = mixerCommand.PcmData;
            TimelineStartFrame = mixerCommand.TargetFrame;
            IsLooping = mixerCommand.ShouldLoop;
        }

        public bool ShouldStartAtTimelineFrame(long playbackGeneration, long timelineFrame)
            => IsActive
                && PlaybackGeneration == playbackGeneration
                && TimelineStartFrame == timelineFrame
                && PcmData != null;

        public bool CanReconstruct(long playbackGeneration)
            => IsActive && PlaybackGeneration == playbackGeneration && PcmData != null;

        public bool MatchesVoice(long voiceIdentifier, long playbackGeneration)
            => IsActive
                && VoiceIdentifier == voiceIdentifier
                && PlaybackGeneration == playbackGeneration;

        public void Deactivate()
        {
            PcmData = null;
        }
    }
}
