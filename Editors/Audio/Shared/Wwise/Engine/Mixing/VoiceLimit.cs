namespace Editors.Audio.Shared.Wwise.Engine.Mixing
{
    // How many voices a node may have sounding at once, and which node is counting them.
    //
    // Wwise puts this on any node in the actor-mixer hierarchy, and a sound inherits whatever its
    // ancestors declare, so the limit that reaches a voice is usually a container's rather than the
    // sound's own — "at most three footsteps at once" is authored on the container that holds the
    // footsteps. Warming works out which node is doing the counting; the pool counts.
    //
    // Zero means no limit, which is what an unauthored node carries.
    internal enum VoiceLimitScope
    {
        GameObject,
        Global
    }

    internal enum VoiceLimitReachedBehaviour
    {
        DiscardOldest,
        DiscardNewest
    }

    internal enum VoiceOverLimitBehaviour
    {
        Kill,
        Virtualise
    }

    internal readonly record struct VoiceLimit(
        uint NodeId,
        int MaximumInstanceCount,
        string BankPath = "",
        VoiceLimitScope Scope = VoiceLimitScope.GameObject,
        VoiceLimitReachedBehaviour ReachedBehaviour = VoiceLimitReachedBehaviour.DiscardNewest,
        VoiceOverLimitBehaviour OverLimitBehaviour = VoiceOverLimitBehaviour.Kill)
    {
        public static readonly VoiceLimit None = new(0, 0);

        public bool IsLimited => MaximumInstanceCount > 0;
    }
}
