namespace Editors.Audio.Shared.Wwise.Engine
{
    // The emitter a post is made on. Switches, and later states, RTPCs, position and listener
    // association, are all held per game object, so two previewed models can hold different switch
    // values instead of sharing one set.
    public readonly record struct GameObjectId(long Value);
}
