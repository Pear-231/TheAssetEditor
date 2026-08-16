using Editors.Audio.Shared.Wwise.Engine.Mixing;

namespace Editors.Audio.Shared.Wwise.Engine.Scheduling
{
    internal sealed class VoiceScheduler
    {
        private readonly ScheduledVoice[] _scheduledVoices;

        public VoiceScheduler(int maximumScheduledVoices)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumScheduledVoices);
            _scheduledVoices = Enumerable.Range(0, maximumScheduledVoices)
                .Select(_ => new ScheduledVoice())
                .ToArray();
        }

        public void AddScheduledVoice(MixerCommand mixerCommand)
        {
            foreach (var scheduledVoice in _scheduledVoices)
            {
                if (scheduledVoice.IsActive)
                    continue;
                scheduledVoice.ApplyScheduleCommand(mixerCommand);
                return;
            }
        }

        public void ActivateVoicesAtTimelineFrame(
            long playbackGeneration,
            long timelineFrame,
            VoicePool voices)
        {
            foreach (var scheduledVoice in _scheduledVoices)
            {
                if (!scheduledVoice.ShouldStartAtTimelineFrame(playbackGeneration, timelineFrame))
                    continue;
                var pcmData = scheduledVoice.PcmData;
                if (pcmData == null)
                    continue;

                voices.TryActivate(
                    scheduledVoice.VoiceIdentifier,
                    scheduledVoice.PlaybackGeneration,
                    pcmData,
                    0,
                    scheduledVoice.IsLooping,
                    isScheduledVoice: true);
            }
        }

        public void ReconstructVoicesAtTimelineFrame(
            long playbackGeneration,
            long timelineFrame,
            VoicePool voices)
        {
            foreach (var scheduledVoice in _scheduledVoices)
            {
                if (!scheduledVoice.CanReconstruct(playbackGeneration))
                    continue;
                var pcmData = scheduledVoice.PcmData;
                if (pcmData == null)
                    continue;

                var elapsedTimelineFrames = timelineFrame - scheduledVoice.TimelineStartFrame;
                var pcmFrameCount = pcmData.Length / (PlaybackFormat.ChannelCount * sizeof(float));
                if (elapsedTimelineFrames > 0
                    && (elapsedTimelineFrames < pcmFrameCount || scheduledVoice.IsLooping))
                {
                    var initialPcmFrame = scheduledVoice.IsLooping && pcmFrameCount > 0
                        ? elapsedTimelineFrames % pcmFrameCount
                        : elapsedTimelineFrames;
                    voices.TryActivate(
                        scheduledVoice.VoiceIdentifier,
                        scheduledVoice.PlaybackGeneration,
                        pcmData,
                        initialPcmFrame,
                        scheduledVoice.IsLooping,
                        isScheduledVoice: true);
                }
            }
        }

        public bool HasScheduledVoiceOtherThan(long voiceIdentifier, long playbackGeneration)
        {
            foreach (var scheduledVoice in _scheduledVoices)
            {
                if (scheduledVoice.IsActive && !scheduledVoice.MatchesVoice(voiceIdentifier, playbackGeneration))
                    return true;
            }
            return false;
        }

        public void CancelScheduledVoice(long voiceIdentifier, long playbackGeneration)
        {
            foreach (var scheduledVoice in _scheduledVoices)
            {
                if (!scheduledVoice.MatchesVoice(voiceIdentifier, playbackGeneration))
                    continue;
                scheduledVoice.Deactivate();
            }
        }

        public void ClearScheduledVoices()
        {
            foreach (var scheduledVoice in _scheduledVoices)
            {
                scheduledVoice.Deactivate();
            }
        }
    }
}
