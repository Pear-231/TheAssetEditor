using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Commands;
using Editors.Audio.Shared.Wwise.Engine.Rendering;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Shared.GameFormats.Wwise.Hirc;

namespace Test.Audio
{
    // What the hierarchy says about a sound, and what the mix does with it.
    //
    // Until phase 11 every voice played at unity gain with no pitch and no filtering, because the
    // parameter seam existed but nothing filled it. These are the tests that say it is filled: the
    // accumulation rules on the way down, and the audible result at the other end.
    public class AuthoredParameterTests
    {
        private static readonly SourceMedia Media = MixerProbe.ConstantPcm(1f, 4_000);

        // Volume, pitch and filtering are relative: every node on the way down adds its own.
        [Test]
        public void RelativePropertiesAccumulateDownTheHierarchy()
        {
            var parameters = WarmedParameters(new FakeHierarchy()
                .WithEvent("impact", 10)
                .WithLayerContainer(10, 1).WithProperty(WwiseProperty.Volume, -6f).WithProperty(WwiseProperty.Pitch, 100f)
                .WithSound(1, Media).WithProperty(WwiseProperty.Volume, -4f).WithProperty(WwiseProperty.Pitch, 50f));

            Assert.That(parameters.VolumeDecibels.Value, Is.EqualTo(-10f).Within(0.0001f));
            Assert.That(parameters.PitchCents.Value, Is.EqualTo(150f).Within(0.0001f));
        }

        // Make-up gain is a second volume in decibels rather than a stage of its own, so it lands in
        // the same sum.
        [Test]
        public void MakeUpGainAddsToTheVolumeRatherThanStandingApartFromIt()
        {
            var parameters = WarmedParameters(new FakeHierarchy()
                .WithEvent("impact", 1)
                .WithSound(1, Media).WithProperty(WwiseProperty.Volume, -6f).WithProperty(WwiseProperty.MakeUpGain, 2f));

            Assert.That(parameters.VolumeDecibels.Value, Is.EqualTo(-4f).Within(0.0001f));
        }

        // Priority is absolute: the nearest node that overrides it decides, and nothing below adds
        // to it.
        [Test]
        public void AnAbsolutePropertyComesFromTheNearestNodeThatOverridesIt()
        {
            var parameters = WarmedParameters(new FakeHierarchy()
                .WithEvent("impact", 10)
                .WithLayerContainer(10, 1).WithProperty(WwiseProperty.Priority, 80f)
                .WithSound(1, Media).WithProperty(WwiseProperty.Priority, 30f));

            Assert.That(parameters.Priority, Is.EqualTo(30f).Within(0.0001f));
        }

        // A bank can store a priority on a node that does not override its parent's, and Wwise does
        // not apply it. Reading the value without the flag would quietly change which sound is cut.
        [Test]
        public void APriorityThatDoesNotOverrideItsParent_IsNotApplied()
        {
            var parameters = WarmedParameters(new FakeHierarchy()
                .WithEvent("impact", 10)
                .WithLayerContainer(10, 1).WithProperty(WwiseProperty.Priority, 80f)
                .WithSound(1, Media).WithProperty(WwiseProperty.Priority, 30f, overridesParent: false));

            Assert.That(parameters.Priority, Is.EqualTo(80f).Within(0.0001f));
        }

        // The walk enters the hierarchy wherever the event points, but a sound hangs under every
        // node above that too, and Wwise accumulates all of them.
        [Test]
        public void PropertiesAboveTheNodeTheEventTargets_StillReachTheSound()
        {
            var parameters = WarmedParameters(new FakeHierarchy()
                .WithEvent("impact", 10)
                .WithActorMixer(20, 10).WithProperty(WwiseProperty.Volume, -6f)
                .WithLayerContainer(10, 1).Under(20)
                .WithSound(1, Media).WithProperty(WwiseProperty.Volume, -4f));

            Assert.That(parameters.VolumeDecibels.Value, Is.EqualTo(-10f).Within(0.0001f));
        }

        // The randomiser ranges accumulate with the values they belong to, and stay open until the
        // instance draws within them: resolving at warm time would make every pass identical.
        [Test]
        public void RandomiserRangesAccumulateAndAreLeftOpenUntilCueTime()
        {
            var parameters = WarmedParameters(new FakeHierarchy()
                .WithEvent("impact", 10)
                .WithLayerContainer(10, 1).WithProperty(WwiseProperty.Volume, 0f, minimum: -2f, maximum: 2f)
                .WithSound(1, Media).WithProperty(WwiseProperty.Volume, 0f, minimum: -1f, maximum: 1f));

            Assert.That(parameters.VolumeDecibels.Minimum, Is.EqualTo(-3f).Within(0.0001f));
            Assert.That(parameters.VolumeDecibels.Maximum, Is.EqualTo(3f).Within(0.0001f));

            var resolver = new ParameterResolver();
            var gains = new HashSet<float>();
            for (var instance = 0; instance < 20; instance++)
                gains.Add(resolver.Resolve(parameters).Volume);

            Assert.That(gains, Has.Count.GreaterThan(1), "every instance drew the same volume");
        }

        // Decibels become linear gain once, on the way out of the hierarchy: -6.02 dB is half the
        // amplitude, and that is what the mix has to carry.
        [Test]
        public void AVolumeInDecibels_BecomesLinearGainInTheMix()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("impact", 1)
                .WithSound(1, MixerProbe.ConstantPcm(0.5f, 4_000)).WithProperty(WwiseProperty.Volume, -6.0206f);

            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            engine.PostEvent("impact", engine.RegisterGameObject("test"));

            Assert.That(outputDevice.Render(8)[0], Is.EqualTo(0.25f).Within(0.001f));
        }

        // Silence is silence rather than a very small number, so a muted branch cannot leak into the
        // bus at all.
        [Test]
        public void AVolumeAtTheFloor_IsSilent()
        {
            Assert.That(AudioLevel.DecibelsToLinear(-96f), Is.Zero);
            Assert.That(AudioLevel.DecibelsToLinear(-200f), Is.Zero);
            Assert.That(AudioLevel.DecibelsToLinear(0f), Is.EqualTo(1f).Within(0.0001f));
        }

        // Pitch and rate conversion are the same operation, so an octave up reads the source twice
        // as fast and the sound lasts half as long.
        [Test]
        public void AuthoredPitch_ReadsTheSourceFasterAndEndsSooner()
        {
            var unpitched = FramesUntilSilence(pitchCents: 0f);
            var octaveUp = FramesUntilSilence(pitchCents: 1200f);

            Assert.That(octaveUp, Is.EqualTo(unpitched / 2).Within(unpitched * 0.05));
        }

        // A low-pass at full is measured on the one signal that is all high frequency: a sample that
        // alternates sign every frame is at Nyquist, and what survives the filter says whether the
        // filter is there at all.
        [Test]
        public void AuthoredLowPassFiltering_TakesTheHighFrequenciesOut()
        {
            var unfiltered = PeakOfNyquistTone(lowPassPercent: 0f);
            var filtered = PeakOfNyquistTone(lowPassPercent: 100f);

            Assert.That(unfiltered, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(filtered, Is.LessThan(unfiltered * 0.1f), "the low-pass let the whole tone through");
        }

        // A high-pass does the opposite to the signal that is all low frequency: a constant.
        [Test]
        public void AuthoredHighPassFiltering_TakesTheConstantOut()
        {
            var unfiltered = PeakOfConstant(highPassPercent: 0f);
            var filtered = PeakOfConstant(highPassPercent: 100f);

            Assert.That(unfiltered, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(filtered, Is.LessThan(unfiltered * 0.1f), "the high-pass let the constant through");
        }

        private static float PeakOfNyquistTone(float lowPassPercent)
        {
            var alternating = new float[8_000];
            for (var sampleIndex = 0; sampleIndex < alternating.Length; sampleIndex++)
                alternating[sampleIndex] = sampleIndex % 4 < 2 ? 0.5f : -0.5f;

            return PeakOf(MixerProbe.ToMedia(alternating), WwiseProperty.LowPassFilter, lowPassPercent);
        }

        private static float PeakOfConstant(float highPassPercent)
            => PeakOf(MixerProbe.ConstantPcm(0.5f, 4_000), WwiseProperty.HighPassFilter, highPassPercent);

        // The first frames of a filter are its transient, so the peak is taken from further in,
        // where what is left is what the filter actually passes.
        private static float PeakOf(SourceMedia media, WwiseProperty property, float percent)
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("tone", 1)
                .WithSound(1, media).WithProperty(property, percent);

            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            engine.PostEvent("tone", engine.RegisterGameObject("test"));

            outputDevice.Render(1_000);
            return MixerProbe.PeakAmplitude(outputDevice.Render(1_000));
        }

        private static int FramesUntilSilence(float pitchCents)
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("tone", 1)
                .WithSound(1, MixerProbe.ConstantPcm(0.5f, 4_000)).WithProperty(WwiseProperty.Pitch, pitchCents);

            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            engine.PostEvent("tone", engine.RegisterGameObject("test"));

            var output = outputDevice.Render(8_000);
            for (var frame = MixerProbe.FrameCount(output) - 1; frame >= 0; frame--)
            {
                if (output[frame * PlaybackFormat.ChannelCount] != 0f)
                    return frame + 1;
            }
            return 0;
        }


        // Priority has had a rule to obey since phase 6 and nothing to obey it with. Now that a node
        // can state one, the rule fires: a newcomer that outranks what is sounding takes its place,
        // and the pool budget is the space they are competing for.
        [Test]
        public void AHigherPriorityNewcomer_TakesThePlaceOfALowerPriorityVoiceInAFullPool()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("quiet", 1)
                .WithSound(1, MixerProbe.ConstantPcm(0.25f, 4_000)).WithProperty(WwiseProperty.Priority, 20f)
                .WithEvent("urgent", 2)
                .WithSound(2, MixerProbe.ConstantPcm(0.25f, 4_000)).WithProperty(WwiseProperty.Priority, 80f);

            var gameObjects = new GameObjectRegistry();
            var gameObject = gameObjects.Find(gameObjects.Register("test"));
            var eventProcessor = new EventProcessor(hierarchy, new NodeWalker(hierarchy), new EngineTelemetry());

            var renderer = new AudioRenderer(maximumVoices: 1);
            renderer.SubmitCommand(EngineCommand.PostEvent(new PlayingId(1, Transport(1)), default, eventProcessor.Warm("quiet", gameObject), new PostCompletionState(new PlayingId(1, Transport(1)))));
            MixerProbe.Render(renderer, 8, 8);
            renderer.SubmitCommand(EngineCommand.PostEvent(new PlayingId(2, Transport(1)), default, eventProcessor.Warm("urgent", gameObject), new PostCompletionState(new PlayingId(2, Transport(1)))));
            MixerProbe.Render(renderer, 8, 8);

            Assert.That(renderer.Telemetry.StolenVoiceCount, Is.EqualTo(1), "the urgent sound did not take the quiet one's place");
            Assert.That(renderer.Telemetry.UnstartedVoiceCount, Is.Zero);
        }

        // The same rule, applied to the other budget: a node's own instance limit. What gives way is
        // the lowest-priority voice counted against that node, not the oldest one in the pool.
        [Test]
        public void AHigherPriorityNewcomer_TakesThePlaceOfALowerPriorityVoiceUnderTheSameInstanceLimit()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("footsteps", 10)
                .WithLayerContainer(10, 1, 2).LimitedTo(1).DiscardNewestOnLimit()
                .WithSound(1, MixerProbe.ConstantPcm(0.25f, 4_000)).WithProperty(WwiseProperty.Priority, 20f)
                .WithSound(2, MixerProbe.ConstantPcm(0.25f, 4_000)).WithProperty(WwiseProperty.Priority, 80f);

            var gameObjects = new GameObjectRegistry();
            var gameObject = gameObjects.Find(gameObjects.Register("test"));
            var eventProcessor = new EventProcessor(hierarchy, new NodeWalker(hierarchy), new EngineTelemetry());
            var quietPlan = eventProcessor.Warm("footsteps", gameObject);

            var renderer = new AudioRenderer(maximumVoices: 8);
            renderer.SubmitCommand(EngineCommand.PostEvent(new PlayingId(1, Transport(1)), default, quietPlan, new PostCompletionState(new PlayingId(1, Transport(1)))));
            MixerProbe.Render(renderer, 8, 8);

            Assert.That(renderer.Telemetry.StolenVoiceCount, Is.EqualTo(1));
        }

        // A newcomer that does not outrank what is sounding is turned away rather than cutting it,
        // which is what keeps a footstep from silencing a death cry it happens to land on.
        [Test]
        public void ALowerPriorityNewcomer_IsTurnedAwayRatherThanCuttingWhatIsSounding()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("urgent", 1)
                .WithSound(1, MixerProbe.ConstantPcm(0.25f, 4_000)).WithProperty(WwiseProperty.Priority, 80f)
                .WithEvent("quiet", 2)
                .WithSound(2, MixerProbe.ConstantPcm(0.25f, 4_000)).WithProperty(WwiseProperty.Priority, 20f);

            var gameObjects = new GameObjectRegistry();
            var gameObject = gameObjects.Find(gameObjects.Register("test"));
            var eventProcessor = new EventProcessor(hierarchy, new NodeWalker(hierarchy), new EngineTelemetry());

            var renderer = new AudioRenderer(maximumVoices: 1);
            renderer.SubmitCommand(EngineCommand.PostEvent(new PlayingId(1, Transport(1)), default, eventProcessor.Warm("urgent", gameObject), new PostCompletionState(new PlayingId(1, Transport(1)))));
            MixerProbe.Render(renderer, 8, 8);
            renderer.SubmitCommand(EngineCommand.PostEvent(new PlayingId(2, Transport(1)), default, eventProcessor.Warm("quiet", gameObject), new PostCompletionState(new PlayingId(2, Transport(1)))));
            MixerProbe.Render(renderer, 8, 8);

            Assert.That(renderer.Telemetry.StolenVoiceCount, Is.Zero, "the quiet sound cut the urgent one");
            Assert.That(renderer.Telemetry.UnstartedVoiceCount, Is.EqualTo(1));
        }

        private static TransportId Transport(long identifier) => new(identifier);

        // The parameters the walk left on the one sound in the plan.
        private static AuthoredParameters WarmedParameters(FakeHierarchy hierarchy)
        {
            var gameObjects = new GameObjectRegistry();
            var gameObject = gameObjects.Find(gameObjects.Register("test"));
            var plan = new EventProcessor(hierarchy, new NodeWalker(hierarchy), new EngineTelemetry())
                .Warm("impact", gameObject);

            Assert.That(plan, Is.Not.Null);
            foreach (var node in plan!.Nodes)
            {
                if (node.Kind == ResolvedNodeKind.Sound)
                    return node.Parameters;
            }

            throw new InvalidOperationException("The plan holds no sound to read parameters from.");
        }
    }
}
