using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Editors.Audio.Shared.Wwise.Engine.Output
{
    // Windows telling the engine that the audio endpoint underneath it has moved.
    //
    // Every callback here arrives on a Windows thread while the driver is still inside the
    // notification, so none of them does anything but pass the news on. Rebuilding a sink from in
    // here would tear down a device the caller is still holding.
    internal sealed class EndpointChangeWatcher(
        Action<string> onRenderEndpointChanged,
        Func<string, bool> isDeviceInUse) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Only the output the engine actually plays through. A change of capture device, or of
            // the communications role, is nothing to do with playback.
            if (flow == DataFlow.Render && role == Role.Multimedia)
                onRenderEndpointChanged($"the default output became {defaultDeviceId}");
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            // Only the device actually being played on. Windows shuffles the state of several
            // endpoints while the default is changed, and reacting to all of them rebuilt the sink
            // seven times for one switch -- heard as a crackle rather than as recovery.
            if (newState != DeviceState.Active && isDeviceInUse(deviceId))
                onRenderEndpointChanged($"device {deviceId} became {newState}");
        }

        public void OnDeviceRemoved(string deviceId)
        {
            if (isDeviceInUse(deviceId))
                onRenderEndpointChanged($"device {deviceId} was removed");
        }

        public void OnDeviceAdded(string deviceId)
        {
            // A device appearing does not move playback on its own: Windows raises
            // OnDefaultDeviceChanged if it becomes the one being used.
        }

        // The endpoint's shared mix format. Renegotiating this underneath a running sink is the one
        // property change that matters here.
        private static readonly Guid DeviceFormatPropertyGroup = new("f19f064d-082c-4e27-bc73-6882a1bb8e4c");

        public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey)
        {
            // Only the mix format.
            //
            // Treating every property change as a reason to rebuild rebuilt the sink twenty-five
            // times in thirty seconds: Windows raises these constantly for peak meters, session
            // state and the like. The playhead fell a second and a half behind wall clock doing it.
            if (propertyKey.formatId != DeviceFormatPropertyGroup || !isDeviceInUse(deviceId))
                return;
            onRenderEndpointChanged($"the mix format of device {deviceId} changed");
        }
    }
}
