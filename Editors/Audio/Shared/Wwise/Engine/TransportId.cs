namespace Editors.Audio.Shared.Wwise.Engine
{
    // Which run of the transport something belongs to: a command, a post, or an output frame the
    // ledger has mapped to a timeline frame.
    //
    // One counter used to mean "which timeline", "which playback instance" and "is this command
    // stale" all at once. PlayingId and GameObjectId took the other two meanings, so what is left
    // here is the transport alone — and it is what makes anything held from a timeline that has
    // since been torn down recognisably stale rather than accidentally matching a reused number.
    public readonly record struct TransportId(long Value)
    {
        // Written out bare, because Super View reports every timing it observes against the
        // timeline it observed it on and a record struct's own formatting would bury the number.
        public override string ToString() => Value.ToString();
    }
}
