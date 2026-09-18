namespace Editors.Audio.Shared.Wwise.Engine.Mixing
{
    // Where a voice is in its own life, which is a different question from what the transport is
    // doing — see SoundPlaybackState. A voice can be Stopping while the transport is Playing.
    //
    // A voice can also be virtualised when an authored instance limit says to advance it silently.
    // Phase 13 owns the broader below-threshold and return-to-physical lifecycle.
    internal enum VoiceState
    {
        Idle,
        Starting,
        Playing,
        Pausing,
        Paused,
        Virtualised,
        Stopping
    }
}
