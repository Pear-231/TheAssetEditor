using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Rendering;

namespace Editors.Audio.Shared.Wwise.Engine.Commands
{
    internal enum EngineCommandType
    {
        // The Wwise verbs the renderer has to be told about. SetSwitch is not among them: switch
        // state is read while warming, on the control thread, so the renderer never sees it.
        PostEvent,
        RetargetPlayingId,
        ScheduleEvent,
        StopPlayingId,
        ConfigureBuses,

        // The scheduling extension and its transport. Wwise has no "play this at 2063 ms" for SFX,
        // because in the game the animation fires and the game posts; Super View has no gameplay,
        // so the timeline is the engine's own.
        CreateTimeline,
        Start,
        PauseTimeline,
        ResumeTimeline,
        StopTimeline,
        SeekTimeline,
        PausePlayingId,
        ResumePlayingId,
        SeekPlayingId,

        // Media handed straight to a voice, for a caller holding decoded audio and no hierarchy —
        // the waveform visualiser, and the audio editor auditioning a WAV through it.
        PlayMedia
    }

    internal readonly record struct EngineCommand
    {
        private EngineCommand(EngineCommandType commandType)
        {
            CommandType = commandType;
        }

        public EngineCommandType CommandType { get; private init; }
        public TransportId TransportId { get; private init; }
        public PlayingId PlayingId { get; private init; }
        public GameObjectId GameObject { get; private init; }
        public ResolvedEvent ResolvedEvent { get; private init; }
        public SourceMedia Media { get; private init; }
        public BusGraph BusGraph { get; private init; }
        public PostCompletionState PostCompletion { get; private init; }
        public long TargetFrame { get; private init; }
        public long TimelineDurationFrames { get; private init; }
        public bool ShouldLoop { get; private init; }
        public long CommandSequence { get; private init; }

        public static EngineCommand PostEvent(
            PlayingId playingId,
            GameObjectId gameObject,
            ResolvedEvent resolvedEvent,
            PostCompletionState postCompletion = null)
            => new(EngineCommandType.PostEvent)
            {
                TransportId = playingId.TransportId,
                PlayingId = playingId,
                GameObject = gameObject,
                ResolvedEvent = resolvedEvent,
                PostCompletion = postCompletion ?? new PostCompletionState(playingId)
            };

        public static EngineCommand ScheduleEvent(
            PlayingId playingId,
            GameObjectId gameObject,
            ResolvedEvent resolvedEvent,
            long timelineFrame,
            PostCompletionState postCompletion = null)
            => new(EngineCommandType.ScheduleEvent)
            {
                TransportId = playingId.TransportId,
                PlayingId = playingId,
                GameObject = gameObject,
                ResolvedEvent = resolvedEvent,
                TargetFrame = timelineFrame,
                PostCompletion = postCompletion ?? new PostCompletionState(playingId)
            };

        public static EngineCommand StopPlayingId(PlayingId playingId)
            => new(EngineCommandType.StopPlayingId)
            {
                TransportId = playingId.TransportId,
                PlayingId = playingId
            };

        public static EngineCommand ConfigureBuses(BusGraph busGraph)
            => new(EngineCommandType.ConfigureBuses) { BusGraph = busGraph };

        public static EngineCommand RetargetPlayingId(
            PlayingId playingId,
            GameObjectId gameObject,
            ResolvedEvent resolvedEvent,
            PostCompletionState postCompletion)
            => new(EngineCommandType.RetargetPlayingId)
            {
                TransportId = playingId.TransportId,
                PlayingId = playingId,
                GameObject = gameObject,
                ResolvedEvent = resolvedEvent,
                PostCompletion = postCompletion
            };

        public static EngineCommand PlayMedia(
            PlayingId playingId,
            GameObjectId gameObject,
            SourceMedia media,
            long startFrame,
            bool shouldLoop,
            PostCompletionState postCompletion = null)
            => new(EngineCommandType.PlayMedia)
            {
                TransportId = playingId.TransportId,
                PlayingId = playingId,
                GameObject = gameObject,
                Media = media,
                TargetFrame = startFrame,
                ShouldLoop = shouldLoop,
                PostCompletion = postCompletion ?? new PostCompletionState(playingId)
            };

        public static EngineCommand CreateTimeline(TransportId transportId, long durationFrames, bool shouldLoop)
            => new(EngineCommandType.CreateTimeline)
            {
                TransportId = transportId,
                TimelineDurationFrames = durationFrames,
                ShouldLoop = shouldLoop
            };

        public static EngineCommand Start(TransportId transportId, long timelineFrame)
            => new(EngineCommandType.Start)
            {
                TransportId = transportId,
                TargetFrame = timelineFrame
            };

        public static EngineCommand PauseTimeline(TransportId transportId)
            => new(EngineCommandType.PauseTimeline) { TransportId = transportId };
        // The frame the device had reached when the resume was asked for. Only the control side can
        // read the device clock, and the voices need it to put themselves back on it.
        public static EngineCommand ResumeTimeline(TransportId transportId, long deviceOutputFrame)
            => new(EngineCommandType.ResumeTimeline)
            {
                TransportId = transportId,
                TargetFrame = deviceOutputFrame
            };
        public static EngineCommand StopTimeline(TransportId transportId)
            => new(EngineCommandType.StopTimeline) { TransportId = transportId };

        public static EngineCommand SeekTimeline(TransportId transportId, long timelineFrame)
            => new(EngineCommandType.SeekTimeline)
            {
                TransportId = transportId,
                TargetFrame = timelineFrame
            };

        public static EngineCommand PausePlayingId(PlayingId playingId)
            => new(EngineCommandType.PausePlayingId) { PlayingId = playingId };

        public static EngineCommand ResumePlayingId(PlayingId playingId, long deviceOutputFrame)
            => new(EngineCommandType.ResumePlayingId)
            {
                PlayingId = playingId,
                TargetFrame = deviceOutputFrame
            };

        public static EngineCommand SeekPlayingId(PlayingId playingId, long mixFrame)
            => new(EngineCommandType.SeekPlayingId)
            {
                PlayingId = playingId,
                TargetFrame = mixFrame
            };

        public EngineCommand WithSequence(long commandSequence)
            => this with { CommandSequence = commandSequence };
    }
}
