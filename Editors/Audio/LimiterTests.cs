using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Shared.GameFormats.Wwise.Hirc;

namespace Test.Audio
{
    // What the limiter does when the mix genuinely arrives over full scale. That is no longer the
    // ordinary case: authored volumes and bus gains are read now, and measured over all 375,197
    // sounds in Warhammer III's 244 banks the accumulated node and bus gain has a median of -13 dB,
    // so it takes about four and a half typical voices rather than two to reach full scale. The
    // limiter stays on the master because the tail of that distribution is real - 3.8% of sounds
    // sit at or above 0 dB and bus chains reach +11 dB - but it is a safety net now rather than a
    // correction for a mix that was played at unity because nothing read its volumes.
    //
    // A limiter that holds the level is doing its job. One that recomputes its gain from each
    // sample is not limiting, it is waveshaping, and the harmonics it generates are heard as a buzz
    // over the top of the audio.
    public class LimiterTests
    {
        private const double ToneFrequency = 220d;

        [TestCase(1.5f)]
        [TestCase(2.4f)]
        [TestCase(4f)]
        public void AMixOverFullScale_IsHeldDownRatherThanDistorted(float amplitude)
        {
            var limiter = new Limiter();
            var samples = Tone(PlaybackFormat.SampleRate / 4, amplitude);

            // Processed in blocks, as the renderer does, so the gain state has to survive them.
            ProcessInBlocks(limiter, samples);

            var settled = Settled(samples);
            var peak = MixerProbe.PeakAmplitude(settled);
            var maximumStep = MixerProbe.MaximumStep(settled);

            // Once the level is held, the output is a sine at the ceiling and steps no harder than
            // such a sine does. A gain that moves within the cycle shows up here and nowhere else.
            var limitedSlewPerFrame = peak * 2f * MathF.PI * (float)ToneFrequency / PlaybackFormat.SampleRate;

            Assert.That(peak, Is.LessThan(1f), "the output must not reach the rail");
            Assert.That(peak, Is.GreaterThan(0.5f), "nor should the level collapse");
            Assert.That(
                maximumStep,
                Is.LessThan(limitedSlewPerFrame * 1.5f),
                $"the output steps {maximumStep / limitedSlewPerFrame:F1}x harder than a sine at this level does, which is distortion rather than limiting");
        }

        // The reassessment phase 13 owed: with authored volumes and bus gains active, is the limiter
        // still correcting a mix that only overloads because nothing reads what the banks state?
        //
        // Five simultaneous cues is what the Throt animation actually peaked at, and full-scale
        // media in phase counts every one of them. At unity that mix arrives at five times full
        // scale and the limiter has to take roughly 14 dB off it. At the -13 dB the banks author at
        // the median it arrives barely over the ceiling, so the reduction is about a decibel. The
        // limiter is therefore kept, and kept on the master, but it is holding the tail of a real
        // distribution rather than standing in for volumes the engine failed to read.
        [Test]
        public void AtAuthoredVolumes_TheLimiterBarelyEngages()
        {
            const int VoiceCount = 5;
            const float MedianAuthoredVolumeDecibels = -13f;

            var unityPeak = PeakBeforeLimiting(VoiceCount, authoredVolumeDecibels: 0f);
            var authoredPeak = PeakBeforeLimiting(VoiceCount, MedianAuthoredVolumeDecibels);

            Assert.Multiple(() =>
            {
                Assert.That(unityPeak, Is.EqualTo(VoiceCount).Within(0.01f), "unity plays every sound at full scale");
                Assert.That(authoredPeak, Is.EqualTo(1.12f).Within(0.02f), "the same cues at the median authored volume");
                Assert.That(
                    20f * MathF.Log10(authoredPeak),
                    Is.LessThan(2f),
                    "the limiter should be taking about a decibel off a dense scene, not the 7.8 dB it took at unity");
            });
        }

        // The peak the mix reached before the limiter touched it, which is what the mix asked for.
        private static float PeakBeforeLimiting(int voiceCount, float authoredVolumeDecibels)
        {
            var hierarchy = new FakeHierarchy();
            for (var soundIndex = 0; soundIndex < voiceCount; soundIndex++)
            {
                hierarchy
                    .WithSound((uint)soundIndex + 1, MixerProbe.ConstantPcm(1f, 512))
                    .WithProperty(WwiseProperty.Volume, authoredVolumeDecibels)
                    .WithEvent($"cue{soundIndex}", (uint)soundIndex + 1);
            }

            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("emitter");
            for (var soundIndex = 0; soundIndex < voiceCount; soundIndex++)
                engine.PostEvent($"cue{soundIndex}", gameObject);

            outputDevice.Render(256);
            return engine.Telemetry.MasterPeakLevel;
        }

        // A single loud transient must not duck everything after it for longer than the release.
        [Test]
        public void ATransient_ReleasesRatherThanHoldingTheMixDown()
        {
            var limiter = new Limiter();
            var samples = Tone(PlaybackFormat.SampleRate, 0.5f);
            for (var channel = 0; channel < PlaybackFormat.ChannelCount; channel++)
                samples[channel] = 8f;

            ProcessInBlocks(limiter, samples);

            var afterTheRelease = Settled(samples);
            Assert.That(
                MixerProbe.PeakAmplitude(afterTheRelease),
                Is.EqualTo(0.5f).Within(0.02f),
                "a quiet mix after a transient should be back at its own level");
        }

        // The renderer hands the limiter one block at a time from a longer buffer, so the tests do
        // the same: the gain has to survive a block boundary, which is where a limiter that reset
        // itself would look fine and sound wrong.
        private static void ProcessInBlocks(Limiter limiter, float[] samples)
        {
            const int BlockFrames = 512;
            var blockSamples = BlockFrames * PlaybackFormat.ChannelCount;
            var block = new float[blockSamples];

            for (var sampleOffset = 0; sampleOffset < samples.Length; sampleOffset += blockSamples)
            {
                // The last block is short, and leaving it unprocessed would leave raw samples in
                // the buffer that the measurement would then read as the limiter having done
                // nothing at all.
                var remainingSamples = Math.Min(blockSamples, samples.Length - sampleOffset);
                Array.Clear(block);
                Array.Copy(samples, sampleOffset, block, 0, remainingSamples);
                limiter.Process(block, remainingSamples / PlaybackFormat.ChannelCount);
                Array.Copy(block, 0, samples, sampleOffset, remainingSamples);
            }
        }

        // The last quarter, by which point the gain has stopped settling in from silence.
        private static float[] Settled(float[] samples)
            => samples[(samples.Length / 4 * 3)..];

        private static float[] Tone(int frameCount, float amplitude)
        {
            var samples = new float[frameCount * PlaybackFormat.ChannelCount];
            for (var frame = 0; frame < frameCount; frame++)
            {
                var value = amplitude * MathF.Sin((float)(2 * Math.PI * ToneFrequency * frame / PlaybackFormat.SampleRate));
                for (var channel = 0; channel < PlaybackFormat.ChannelCount; channel++)
                    samples[frame * PlaybackFormat.ChannelCount + channel] = value;
            }
            return samples;
        }
    }
}
