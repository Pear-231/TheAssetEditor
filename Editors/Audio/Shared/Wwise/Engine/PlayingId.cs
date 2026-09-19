namespace Editors.Audio.Shared.Wwise.Engine
{
    // One per post: what the game layer holds on to so it can ask where a post has got to, or stop
    // it. A single post can start several voices — every layer of a layer container sounds at once —
    // so this identifies the post rather than any one of them.
    //
    // The transport rides along because it is what makes a handle from a torn down timeline
    // recognisably stale rather than accidentally matching a reused identifier.
    public readonly record struct PlayingId(long Value, TransportId TransportId);
}
