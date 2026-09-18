using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Commands;

namespace Editors.Audio.Shared.Wwise.Engine.Scheduling
{
    // An event waiting for its frame on the timeline, with the plan warming left behind it.
    //
    // What is held here is the event, not a sound: dropping the pre-resolved buffer is what lets a
    // container pick fresh on every pass, and it is why a looping animation varies its foley the
    // way the game does instead of replaying one bit-identical recording.
    internal sealed class ScheduledEvent
    {
        public bool IsActive => Plan != null;
        public PlayingId PlayingId { get; private set; }
        public GameObjectId GameObject { get; private set; }
        public PostCompletionState PostCompletion { get; private set; }
        public ResolvedEvent Plan { get; private set; }
        public long TimelineStartFrame { get; private set; }

        public void ApplyScheduleCommand(EngineCommand engineCommand)
        {
            PlayingId = engineCommand.PlayingId;
            GameObject = engineCommand.GameObject;
            PostCompletion = engineCommand.PostCompletion;
            Plan = engineCommand.ResolvedEvent;
            TimelineStartFrame = engineCommand.TargetFrame;
        }

        public bool ShouldStartAtTimelineFrame(TransportId transportId, long timelineFrame)
            => IsActive
                && PlayingId.TransportId == transportId
                && TimelineStartFrame == timelineFrame;

        public bool CanReconstruct(TransportId transportId)
            => IsActive && PlayingId.TransportId == transportId;

        public bool Matches(PlayingId playingId) => IsActive && PlayingId == playingId;

        public void Deactivate()
        {
            Plan = null;
            PostCompletion = null;
        }
    }
}
