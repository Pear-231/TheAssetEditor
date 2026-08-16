namespace Editors.Audio.Shared.Wwise.Engine.Mixing
{
    internal enum MixerCommandType
    {
        Play,
        CreateTimeline,
        Schedule,
        Start,
        Pause,
        Resume,
        Stop,
        Seek,
        CancelVoice
    }

    internal readonly record struct MixerCommand
    {
        private MixerCommand(MixerCommandType commandType)
        {
            CommandType = commandType;
        }

        public MixerCommandType CommandType { get; private init; }
        public long PlaybackGeneration { get; private init; }
        public long VoiceIdentifier { get; private init; }
        public byte[] PcmData { get; private init; }
        public long TargetFrame { get; private init; }
        public long TimelineDurationFrames { get; private init; }
        public bool ShouldLoop { get; private init; }
        public long CommandSequence { get; private init; }

        public static MixerCommand Play(long playbackGeneration, long voiceIdentifier, byte[] pcmData, long startFrame, bool shouldLoop)
            => new(MixerCommandType.Play)
            {
                PlaybackGeneration = playbackGeneration,
                VoiceIdentifier = voiceIdentifier,
                PcmData = pcmData,
                TargetFrame = startFrame,
                ShouldLoop = shouldLoop
            };

        public static MixerCommand CreateTimeline(long playbackGeneration, long durationFrames, bool shouldLoop)
            => new(MixerCommandType.CreateTimeline)
            {
                PlaybackGeneration = playbackGeneration,
                TimelineDurationFrames = durationFrames,
                ShouldLoop = shouldLoop
            };

        public static MixerCommand Schedule(long playbackGeneration, long voiceIdentifier, byte[] pcmData, long timelineFrame, bool shouldLoop)
            => new(MixerCommandType.Schedule)
            {
                PlaybackGeneration = playbackGeneration,
                VoiceIdentifier = voiceIdentifier,
                PcmData = pcmData,
                TargetFrame = timelineFrame,
                ShouldLoop = shouldLoop
            };

        public static MixerCommand Start(long playbackGeneration, long timelineFrame)
            => new(MixerCommandType.Start)
            {
                PlaybackGeneration = playbackGeneration,
                TargetFrame = timelineFrame
            };

        public static MixerCommand Pause() => new(MixerCommandType.Pause);
        public static MixerCommand Resume() => new(MixerCommandType.Resume);
        public static MixerCommand Stop() => new(MixerCommandType.Stop);

        public static MixerCommand Seek(long playbackGeneration, long timelineFrame)
            => new(MixerCommandType.Seek)
            {
                PlaybackGeneration = playbackGeneration,
                TargetFrame = timelineFrame
            };

        public static MixerCommand Cancel(long playbackGeneration, long voiceIdentifier)
            => new(MixerCommandType.CancelVoice)
            {
                PlaybackGeneration = playbackGeneration,
                VoiceIdentifier = voiceIdentifier
            };

        public MixerCommand WithSequence(long commandSequence)
            => this with { CommandSequence = commandSequence };
    }
}
