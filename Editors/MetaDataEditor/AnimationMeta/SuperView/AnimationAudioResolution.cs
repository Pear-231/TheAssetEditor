namespace Editors.AnimationMeta.SuperView
{
    // What the game layer worked out for one sound event: the battle action event it maps to, the
    // switch groups that event depends on and the values this unit resolves them to.
    //
    // No single sound any more. A container has no one answer until it picks, and the pick happens
    // at cue time inside the engine, so what can be said here is the whole set the event could
    // reach as the switches currently stand.
    internal sealed record AnimationAudioResolution(
        string? ActionEvent,
        IReadOnlyList<string> SwitchGroups,
        IReadOnlyDictionary<string, string> SwitchValues,
        IReadOnlyList<uint> PlayableSourceIds);
}
