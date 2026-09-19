namespace Editors.Audio.Shared.Wwise.Engine.Rendering
{
    // What the transport should do once it has finished ramping down to silence. Pause keeps
    // everything where it is so resume can carry on from it; Stop tears the timeline down.
    internal enum TransportAction
    {
        None,
        Pause,
        Stop
    }
}
