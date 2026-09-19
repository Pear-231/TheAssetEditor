namespace Editors.Audio.Shared.Wwise.Engine.Rendering
{
    // What a rendered block did to the transport: how many of its frames the timeline actually
    // passed through, and whether playback ran out inside it.
    //
    // The two are not the same number. A timeline passes through the whole block, because the block
    // was cut short at whatever was due next. A transport free-running behind an immediate post
    // passes through only the frames that had audio in them, and the frames after that are the ones
    // it ran out on.
    internal readonly record struct TransportAdvance(int AdvancingFrameCount, bool HasCompleted)
    {
        public static TransportAdvance Running(int advancingFrameCount) => new(advancingFrameCount, false);
        public static TransportAdvance Completed(int advancingFrameCount) => new(advancingFrameCount, true);
    }
}
