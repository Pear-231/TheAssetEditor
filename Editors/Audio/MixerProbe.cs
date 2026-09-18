using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Rendering;

namespace Test.Audio
{
    // Builds the signals the renderer is fed and measures what came back out.
    //
    // The measurements are deliberately gain agnostic: they compare the output against a clean
    // stretch of itself rather than against a literal level, so they keep their meaning once the
    // 0.8/sqrt(n) divisor and the mix clamp are replaced by a bus gain and a limiter.
    internal static class MixerProbe
    {
        // A whole number of cycles of this period loops seamlessly, so whatever discontinuity a
        // loop point shows is the engine's rather than the content's. The phase offset keeps that
        // loop point away from a zero crossing, where nothing would show at all.
        public const int SeamlessTonePeriodFrames = 128;
        private const float SeamlessTonePhaseRadians = 0.7f;

        // Deliberately not a whole number of frames per cycle. A probe whose period divides into
        // the offsets under test lands every "mid-waveform" measurement on a zero crossing and
        // measures nothing.
        public const double ProbeToneFrequency = 317d;

        public static float[] Render(AudioRenderer renderer, int frameCount, int chunkFrames)
        {
            var output = new float[frameCount * PlaybackFormat.ChannelCount];
            var renderedFrameCount = 0;
            while (renderedFrameCount < frameCount)
            {
                var chunkFrameCount = Math.Min(chunkFrames, frameCount - renderedFrameCount);
                renderer.Render(
                    output,
                    renderedFrameCount * PlaybackFormat.ChannelCount,
                    chunkFrameCount * PlaybackFormat.ChannelCount);
                renderedFrameCount += chunkFrameCount;
            }
            return output;
        }

        // The smallest thing the upper engine can hand the renderer: one event, one sound, its
        // media already attached. Tests that are measuring the mix rather than the walk schedule
        // through this so they still go down the path a real cue takes.
        public static ResolvedEvent SingleSoundEvent(SourceMedia media, uint nodeId = 1)
        {
            var builder = new ResolvedEventBuilder();
            builder.AddRoot(builder.AddSound(nodeId, nodeId, media, VoiceLimit.None));
            return builder.Build($"probe-event-{nodeId}");
        }

        public static SourceMedia ConstantPcm(float value, int frames)
            => ToMedia(Enumerable.Repeat(value, frames * PlaybackFormat.ChannelCount).ToArray());

        public static SourceMedia SilentPcm(int frames)
            => ConstantPcm(0f, frames);

        public static SourceMedia SeamlessTonePcm(int frames, float amplitude)
        {
            if (frames % SeamlessTonePeriodFrames != 0)
                throw new ArgumentException(
                    $"A seamless tone must hold a whole number of {SeamlessTonePeriodFrames} frame cycles.",
                    nameof(frames));

            return TonePcm(
                frames,
                amplitude,
                frame => 2 * Math.PI * frame / SeamlessTonePeriodFrames + SeamlessTonePhaseRadians);
        }

        public static SourceMedia ProbeTonePcm(int frames, float amplitude)
            => TonePcm(frames, amplitude, frame => 2 * Math.PI * ProbeToneFrequency * frame / PlaybackFormat.SampleRate);

        // Already at the mix rate and channel count, so the voice's resampler and panner pass it
        // straight through and a test measuring the mix is measuring only what it meant to.
        public static SourceMedia ToMedia(float[] interleavedSamples)
            => ToMedia(interleavedSamples, PlaybackFormat.ChannelCount, PlaybackFormat.SampleRate);

        public static SourceMedia ToMedia(float[] interleavedSamples, int channelCount, int sampleRate)
            => new(
                $"probe-{Guid.NewGuid():N}",
                interleavedSamples,
                channelCount,
                sampleRate);

        // The frames where the output goes from silent to sounding. Cue accuracy is asserted
        // against these rather than against a level, so the assertion outlives any gain change.
        public static long[] OnsetFrames(float[] output)
        {
            var onsetFrames = new List<long>();
            var wasSounding = false;
            for (var frame = 0; frame < FrameCount(output); frame++)
            {
                var isSounding = false;
                for (var channel = 0; channel < PlaybackFormat.ChannelCount; channel++)
                    isSounding |= output[frame * PlaybackFormat.ChannelCount + channel] != 0f;

                if (isSounding && !wasSounding)
                    onsetFrames.Add(frame);
                wasSounding = isSounding;
            }
            return [.. onsetFrames];
        }

        public static float MaximumStep(float[] output)
            => MaximumStep(output, 0, FrameCount(output));

        // The largest jump between one frame and the next, per channel. A transport cut that the
        // engine has not ramped through shows up here as a step far larger than the waveform's
        // own slew.
        public static float MaximumStep(float[] output, int firstFrame, int frameCount)
        {
            var maximumStep = 0f;
            for (var frame = firstFrame + 1; frame < firstFrame + frameCount; frame++)
            {
                for (var channel = 0; channel < PlaybackFormat.ChannelCount; channel++)
                {
                    var step = MathF.Abs(
                        output[frame * PlaybackFormat.ChannelCount + channel]
                        - output[(frame - 1) * PlaybackFormat.ChannelCount + channel]);
                    if (step > maximumStep)
                        maximumStep = step;
                }
            }
            return maximumStep;
        }

        public static float PeakAmplitude(float[] output)
        {
            var peakAmplitude = 0f;
            foreach (var sample in output)
                peakAmplitude = MathF.Max(peakAmplitude, MathF.Abs(sample));
            return peakAmplitude;
        }

        public static int ClippedSampleCount(float[] output)
            => output.Count(sample => MathF.Abs(sample) >= 1f);

        public static int FrameCount(float[] output)
            => output.Length / PlaybackFormat.ChannelCount;

        private static SourceMedia TonePcm(int frames, float amplitude, Func<int, double> phaseAtFrame)
        {
            var interleavedSamples = new float[frames * PlaybackFormat.ChannelCount];
            for (var frame = 0; frame < frames; frame++)
            {
                var sample = amplitude * MathF.Sin((float)phaseAtFrame(frame));
                for (var channel = 0; channel < PlaybackFormat.ChannelCount; channel++)
                    interleavedSamples[frame * PlaybackFormat.ChannelCount + channel] = sample;
            }
            return ToMedia(interleavedSamples);
        }
    }
}
