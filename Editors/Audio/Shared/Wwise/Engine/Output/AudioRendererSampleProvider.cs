using Editors.Audio.Shared.Wwise.Engine.Rendering;
using NAudio.Wave;

namespace Editors.Audio.Shared.Wwise.Engine.Output
{
    // The renderer as the output device wants to see it.
    //
    // Being an NAudio interface is a sink concern rather than the engine's identity, so it lives
    // here beside the device instead of on the renderer, which now has an upper engine to drive and
    // no business also being a buffer source for one particular audio library.
    internal sealed class AudioRendererSampleProvider(AudioRenderer audioRenderer) : ISampleProvider
    {
        private readonly AudioRenderer _audioRenderer = audioRenderer;

        public WaveFormat WaveFormat => PlaybackFormat.WaveFormat;

        public int Read(float[] destinationBuffer, int destinationOffset, int requestedSampleCount)
            => _audioRenderer.Render(destinationBuffer, destinationOffset, requestedSampleCount);
    }
}
