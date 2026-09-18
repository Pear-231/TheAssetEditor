namespace Editors.Audio.Shared.Wwise.Engine.Output
{
    internal readonly record struct AudioDevicePosition(long PositionBytes, int AverageBytesPerSecond);
}
