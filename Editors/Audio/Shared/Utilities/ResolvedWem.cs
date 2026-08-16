namespace Editors.Audio.Shared.Utilities
{
    public sealed record ResolvedWem(uint Id, string FilePath, bool IsDidx, byte[] Data);
}
