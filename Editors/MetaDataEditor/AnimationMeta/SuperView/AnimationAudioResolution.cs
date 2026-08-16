using Editors.Audio.Shared.Wwise.Engine.Cache;

namespace Editors.AnimationMeta.SuperView
{
    internal sealed record AnimationAudioResolution(
        string? ActionEvent,
        IReadOnlyList<string> SwitchGroups,
        IReadOnlyDictionary<string, string> SwitchValues,
        uint? WemId,
        string? WemFilePath,
        bool IsWemDidx,
        SoundEngineCacheEntry? Audio);
}
