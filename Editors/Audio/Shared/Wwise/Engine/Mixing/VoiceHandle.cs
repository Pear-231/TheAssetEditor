namespace Editors.Audio.Shared.Wwise.Engine.Mixing
{
    public readonly record struct VoiceHandle(long VoiceIdentifier, long PlaybackGeneration);

    internal readonly record struct VoiceCompletion(VoiceHandle VoiceHandle, long AbsoluteOutputFrame);
}
