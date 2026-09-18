using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Media;

namespace Test.Audio
{
    // Real bank media is rarely what the rest of the suite builds by hand. A WEM is usually mono and
    // need not be authored at the mix rate, so these drive the whole Super View path — bank, event,
    // timeline, transport, voice chain, sink — with media that has to be resampled and placed rather
    // than passed straight through.
    //
    // They assert continuity rather than a level: an output that steps far harder than the waveform
    // itself does is what a click, or a train of them, actually is.
    public class SoundEngineSourceFormatTests
    {
        private const double ToneFrequency = 220d;
        private const float ToneAmplitude = 0.5f;

        // The steepest the tone itself ever moves between two mix frames. Anything much beyond this
        // did not come from the audio.
        private static readonly float NaturalStepPerMixFrame =
            ToneAmplitude * 2f * MathF.PI * (float)ToneFrequency / PlaybackFormat.SampleRate;

        [TestCase(48_000, 2)]
        [TestCase(48_000, 1)]
        [TestCase(44_100, 1)]
        [TestCase(22_050, 1)]
        public void ASoundAuthoredInAnyFormat_ReachesTheMixWithoutSteppingIt(int sampleRate, int channelCount)
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("cue", 1)
                .WithSound(1, Tone(sampleRate * 2, sampleRate, channelCount)));
            var unit = engine.RegisterGameObject("unit");

            var transport = engine.CreateTimeline(TimeSpan.FromSeconds(2), shouldLoop: false);
            engine.ScheduleEvent("cue", unit, TimeSpan.Zero, transport);
            engine.Start(transport);

            var output = outputDevice.Render(24_000);

            Assert.That(MixerProbe.PeakAmplitude(output), Is.GreaterThan(0.1f), "the cue never sounded");
            Assert.That(
                MixerProbe.MaximumStep(output),
                Is.LessThan(NaturalStepPerMixFrame * 3f),
                "the output steps harder than the waveform does, which is what a click sounds like");
        }

        // Super View does more than schedule one cue at zero: it loops, it starts from wherever the
        // playhead already is, and it fires several cues that overlap each other.
        [Test]
        public void ALoopingTimelineStartedPartway_OverlapsItsCuesWithoutSteppingTheMix()
        {
            var media = Tone(24_000, PlaybackFormat.SampleRate, 1);
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("first", 1)
                .WithEvent("second", 2)
                .WithEvent("third", 3)
                .WithSound(1, media)
                .WithSound(2, media)
                .WithSound(3, media));
            var unit = engine.RegisterGameObject("unit");

            var transport = engine.CreateTimeline(TimeSpan.FromSeconds(1), shouldLoop: true);
            engine.ScheduleEvent("first", unit, TimeSpan.Zero, transport);
            engine.ScheduleEvent("second", unit, TimeSpan.FromMilliseconds(313), transport);
            engine.ScheduleEvent("third", unit, TimeSpan.FromMilliseconds(717), transport);
            engine.Start(transport, TimeSpan.FromMilliseconds(150));

            var output = outputDevice.Render(PlaybackFormat.SampleRate * 3);

            // Three overlapping voices sum past the limiter ceiling, so the step allowance is wider
            // than the single-voice case — but a cut would still be an order of magnitude past it.
            Assert.That(
                MixerProbe.MaximumStep(output),
                Is.LessThan(NaturalStepPerMixFrame * 4f),
                "cues that overlap must sum, not cut each other");
            Assert.That(engine.Telemetry.SilentCueCount, Is.Zero);
            Assert.That(engine.Telemetry.UnstartedVoiceCount, Is.Zero);
        }

        private static SourceMedia Tone(int frameCount, int sampleRate, int channelCount)
        {
            var samples = new float[frameCount * channelCount];
            for (var frame = 0; frame < frameCount; frame++)
            {
                var value = ToneAmplitude * MathF.Sin((float)(2 * Math.PI * ToneFrequency * frame / sampleRate));
                for (var channel = 0; channel < channelCount; channel++)
                    samples[frame * channelCount + channel] = value;
            }
            return MixerProbe.ToMedia(samples, channelCount, sampleRate);
        }
    }
}
