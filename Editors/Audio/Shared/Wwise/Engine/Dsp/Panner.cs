using Editors.Audio.Shared.Wwise.Engine.Media;
using System.Numerics;

namespace Editors.Audio.Shared.Wwise.Engine.Dsp
{
    // Routes an authored speaker layout into the stereo output. Mono and stereo retain direct fast
    // paths; wider layouts use the WAVE speaker mask, with every declared channel contributing to
    // the result. Unknown masks fall back to stable left/right pairs instead of dropping channels.
    internal sealed class Panner
    {
        private const uint FrontLeft = 0x0001;
        private const uint FrontRight = 0x0002;
        private const uint FrontCentre = 0x0004;
        private const uint LowFrequency = 0x0008;
        private const uint BackLeft = 0x0010;
        private const uint BackRight = 0x0020;
        private const uint FrontLeftOfCentre = 0x0040;
        private const uint FrontRightOfCentre = 0x0080;
        private const uint BackCentre = 0x0100;
        private const uint SideLeft = 0x0200;
        private const uint SideRight = 0x0400;
        private const float CentreGain = 0.70710678f;
        private const float LfeGain = 0.5f;

        public void ReadStereo(
            SourceMedia media,
            Resampler resampler,
            int loopStartFrame,
            int loopEndFrame,
            out float left,
            out float right)
        {
            if (media.ChannelCount == 1)
            {
                left = right = resampler.ReadChannel(media, 0, loopStartFrame, loopEndFrame);
                return;
            }
            if (media.ChannelCount == 2 && (media.ChannelMask == 0 || media.ChannelMask == (FrontLeft | FrontRight)))
            {
                left = resampler.ReadChannel(media, 0, loopStartFrame, loopEndFrame);
                right = resampler.ReadChannel(media, 1, loopStartFrame, loopEndFrame);
                return;
            }

            left = 0f;
            right = 0f;
            var mask = media.ChannelMask;
            for (var sourceChannel = 0; sourceChannel < media.ChannelCount; sourceChannel++)
            {
                var sample = resampler.ReadChannel(media, sourceChannel, loopStartFrame, loopEndFrame);
                var speaker = TakeNextSpeaker(ref mask);
                if (speaker == 0)
                {
                    if ((sourceChannel & 1) == 0)
                        left += sample * CentreGain;
                    else
                        right += sample * CentreGain;
                    continue;
                }

                switch (speaker)
                {
                    case FrontLeft:
                        left += sample;
                        break;
                    case FrontRight:
                        right += sample;
                        break;
                    case LowFrequency:
                        left += sample * LfeGain;
                        right += sample * LfeGain;
                        break;
                    case FrontCentre:
                    case BackCentre:
                        left += sample * CentreGain;
                        right += sample * CentreGain;
                        break;
                    case BackLeft:
                    case SideLeft:
                    case FrontLeftOfCentre:
                        left += sample * CentreGain;
                        break;
                    case BackRight:
                    case SideRight:
                    case FrontRightOfCentre:
                        right += sample * CentreGain;
                        break;
                    default:
                        left += sample * CentreGain;
                        right += sample * CentreGain;
                        break;
                }
            }
        }

        private static uint TakeNextSpeaker(ref uint mask)
        {
            if (mask == 0)
                return 0;
            var speaker = mask & (uint)-(int)mask;
            mask &= ~speaker;
            return speaker;
        }

        public static void ApplyPosition(Vector3 emitter, ReadOnlySpan<Vector3> listeners, ref float left, ref float right)
        {
            if (listeners.IsEmpty)
                return;
            var listener = listeners[0];
            var nearestDistanceSquared = Vector3.DistanceSquared(emitter, listener);
            for (var listenerIndex = 1; listenerIndex < listeners.Length; listenerIndex++)
            {
                var distanceSquared = Vector3.DistanceSquared(emitter, listeners[listenerIndex]);
                if (distanceSquared >= nearestDistanceSquared)
                    continue;
                listener = listeners[listenerIndex];
                nearestDistanceSquared = distanceSquared;
            }
            var relative = emitter - listener;
            var distance = relative.Length();
            var pan = distance <= float.Epsilon ? 0f : Math.Clamp(relative.X / distance, -1f, 1f);
            var angle = (pan + 1f) * MathF.PI / 4f;
            left *= MathF.Cos(angle) * MathF.Sqrt(2f);
            right *= MathF.Sin(angle) * MathF.Sqrt(2f);
        }
    }
}
