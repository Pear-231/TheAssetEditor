using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Shared.GameFormats.Wwise.Enums;

namespace Test.Audio
{
    public class ContainerFidelityTests
    {
        private static readonly SourceMedia Media = MixerProbe.ConstantPcm(0.25f, 4_000);

        [Test]
        public void ShuffleExhaustsEveryEntryBeforeAnyEntryRepeats()
        {
            var (plan, _) = Warm(new FakeHierarchy()
                .WithEvent("shuffle", 10)
                .WithRandomContainer(10, avoidRepeatCount: 1, 1, 2, 3).AsShuffle()
                .WithSound(1, Media)
                .WithSound(2, Media)
                .WithSound(3, Media));

            var firstBag = new[] { SelectOne(plan), SelectOne(plan), SelectOne(plan) };
            Assert.That(firstBag.Distinct().Count(), Is.EqualTo(3));

            var firstAfterReset = SelectOne(plan);
            Assert.That(firstAfterReset, Is.Not.EqualTo(firstBag[^1]), "shuffle reset ignored the authored repetition exclusion");
        }

        [Test]
        public void GlobalContainerScopeSharesItsCursorAcrossGameObjects()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("sequence", 10)
                .WithSequenceContainer(10, 1, 2).WithGlobalScope()
                .WithSound(1, Media)
                .WithSound(2, Media);
            var registry = new GameObjectRegistry();
            var firstObject = registry.Find(registry.Register("first"));
            var secondObject = registry.Find(registry.Register("second"));
            var processor = new EventProcessor(hierarchy, new NodeWalker(hierarchy), new EngineTelemetry());

            Assert.That(SelectOne(processor.Warm("sequence", firstObject)), Is.EqualTo(1));
            Assert.That(SelectOne(processor.Warm("sequence", secondObject)), Is.EqualTo(2));
        }

        [Test]
        public void GameObjectContainerScopeKeepsASeparateCursorForEachGameObject()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("sequence", 10)
                .WithSequenceContainer(10, 1, 2)
                .WithSound(1, Media)
                .WithSound(2, Media);
            var registry = new GameObjectRegistry();
            var firstObject = registry.Find(registry.Register("first"));
            var secondObject = registry.Find(registry.Register("second"));
            var processor = new EventProcessor(hierarchy, new NodeWalker(hierarchy), new EngineTelemetry());

            Assert.That(SelectOne(processor.Warm("sequence", firstObject)), Is.EqualTo(1));
            Assert.That(SelectOne(processor.Warm("sequence", secondObject)), Is.EqualTo(1));
        }

        [TestCase(0f, -96f)]
        [TestCase(50f, -6f)]
        [TestCase(100f, 0f)]
        public void LayerCurveControlsWhetherAndAtWhatGainAChildSounds(float parameterValue, float expectedDecibels)
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("engine", 10)
                .WithLayerContainer(10, 1)
                .WithLayerCurve("rpm", 1, (0f, -96f, 4), (50f, -6f, 4), (100f, 0f, 4))
                .WithSound(1, Media);
            var (plan, gameObject) = Warm(hierarchy, (state) => state.SetGameParameter("rpm", parameterValue));

            if (expectedDecibels <= AudioLevel.SilenceDecibels)
            {
                Assert.That(Select(plan), Is.Empty);
                return;
            }

            var selected = Select(plan);
            Assert.That(selected, Has.Length.EqualTo(1));
            Assert.That(selected[0].Parameters.VolumeDecibels.Value, Is.EqualTo(expectedDecibels).Within(0.02f));
        }

        [Test]
        public void BankIdentitySeparatesOtherwiseIdenticalContainerKeys()
        {
            var registry = new GameObjectRegistry();
            var gameObject = registry.Find(registry.Register("test"));
            var first = gameObject.GetInstanceState("first.bnk", 10, 0, 2, isGlobal: false, isShuffle: false);
            var second = gameObject.GetInstanceState("second.bnk", 10, 0, 2, isGlobal: false, isShuffle: false);

            first.SequenceIndex = 1;
            Assert.That(second.SequenceIndex, Is.Zero);
        }

        [Test]
        public void StepAndContinuousSequencesProduceDifferentPosts()
        {
            var step = Warm(new FakeHierarchy()
                .WithEvent("sequence", 10)
                .WithSequenceContainer(10, 1, 2)
                .WithSound(1, MixerProbe.ConstantPcm(0.1f, 100))
                .WithSound(2, MixerProbe.ConstantPcm(0.2f, 100)), eventName: "sequence").Plan;
            var continuous = Warm(new FakeHierarchy()
                .WithEvent("sequence", 10)
                .WithSequenceContainer(10, 1, 2).AsContinuous(loopCount: 2)
                .WithSound(1, MixerProbe.ConstantPcm(0.1f, 100))
                .WithSound(2, MixerProbe.ConstantPcm(0.2f, 100)), eventName: "sequence").Plan;

            Assert.That(Select(step).Select(sound => sound.NodeId), Is.EqualTo(new uint[] { 1 }));
            var selected = Select(continuous);
            Assert.That(selected.Select(sound => sound.NodeId), Is.EqualTo(new uint[] { 1, 2, 1, 2 }));
            Assert.That(selected.Select(sound => sound.ContainerDelayFrames), Is.EqualTo(new[] { 0, 100, 200, 300 }));
        }

        [Test]
        public void LoopRandomisationAndTransitionModeChangeContinuousTiming()
        {
            var plan = Warm(new FakeHierarchy()
                .WithEvent("sequence", 10)
                .WithSequenceContainer(10, 1).AsContinuous(loopCount: 1)
                .WithLoopRandomisation(1, 1)
                .WithTransition(AkTransitionMode.Delay, 1f)
                .WithSound(1, MixerProbe.ConstantPcm(0.1f, 100)), eventName: "sequence").Plan;

            var selected = Select(plan);
            Assert.That(selected, Has.Length.EqualTo(2));
            Assert.That(selected[1].ContainerDelayFrames, Is.EqualTo(148), "100 media frames plus the authored 1 ms delay");
        }

        [Test]
        public void AlwaysResetPlaylistStartsEachStepPostAtTheFirstEntry()
        {
            var (plan, _) = Warm(new FakeHierarchy()
                .WithEvent("sequence", 10)
                .WithSequenceContainer(10, 1, 2).AlwaysResetPlaylist()
                .WithSound(1, Media)
                .WithSound(2, Media), eventName: "sequence");

            Assert.That(SelectOne(plan), Is.EqualTo(1));
            Assert.That(SelectOne(plan), Is.EqualTo(1));
        }

        [Test]
        public void AContinuouslyValidatedSwitchCrossFadesToItsNewBranch()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("engine", 10)
                .WithSwitchContainer(10, "mode", "idle", ("idle", new uint[] { 1 }), ("drive", new uint[] { 2 }))
                .WithContinuousSwitchValidation(2, 2, 1, 2)
                .WithSound(1, MixerProbe.ConstantPcm(0.25f, 4_000))
                .WithSound(2, MixerProbe.ConstantPcm(0.5f, 4_000));
            var output = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(output);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("car");
            engine.PostEvent("engine", gameObject);
            var initialFade = output.Render(120);
            Assert.That(initialFade[^1], Is.EqualTo(0.25f).Within(0.0001f));

            engine.SetSwitch("mode", "drive", gameObject);
            var transition = output.Render(120);

            Assert.That(transition[0], Is.GreaterThan(0.24f).And.LessThan(0.51f));
            Assert.That(transition[^1], Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ANonContinuousSwitchKeepsTheBranchChosenWhenItWasPosted()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("engine", 10)
                .WithSwitchContainer(10, "mode", "idle", ("idle", new uint[] { 1 }), ("drive", new uint[] { 2 }))
                .WithSound(1, MixerProbe.ConstantPcm(0.25f, 4_000))
                .WithSound(2, MixerProbe.ConstantPcm(0.5f, 4_000));
            var output = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(output);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("car");
            engine.PostEvent("engine", gameObject);
            output.Render(8);

            engine.SetSwitch("mode", "drive", gameObject);

            Assert.That(output.Render(16)[^1], Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void AmplitudeAndPowerCrossfadesCarryDifferentGainCurves()
        {
            var amplitude = Warm(new FakeHierarchy()
                .WithEvent("sequence", 10)
                .WithSequenceContainer(10, 1, 2).AsContinuous()
                .WithTransition(AkTransitionMode.CrossFadeAmp, 1f)
                .WithSound(1, MixerProbe.ConstantPcm(0.25f, 100))
                .WithSound(2, MixerProbe.ConstantPcm(0.25f, 100)), eventName: "sequence").Plan;
            var power = Warm(new FakeHierarchy()
                .WithEvent("sequence", 10)
                .WithSequenceContainer(10, 1, 2).AsContinuous()
                .WithTransition(AkTransitionMode.CrossFadePower, 1f)
                .WithSound(1, MixerProbe.ConstantPcm(0.25f, 100))
                .WithSound(2, MixerProbe.ConstantPcm(0.25f, 100)), eventName: "sequence").Plan;

            var amplitudeSelection = Select(amplitude);
            var powerSelection = Select(power);
            Assert.That(amplitudeSelection[0].ContainerEndFadeOutFrames, Is.EqualTo(48));
            Assert.That(amplitudeSelection[1].ContainerDelayFrames, Is.EqualTo(52));
            Assert.That(amplitudeSelection, Has.All.Property(nameof(SelectedSound.UsesEqualPowerCrossfade)).False);
            Assert.That(powerSelection, Has.All.Property(nameof(SelectedSound.UsesEqualPowerCrossfade)).True);
        }

        [Test]
        public void ContainerSelectionDoesNotAllocateAtCueTime()
        {
            var plan = Warm(new FakeHierarchy()
                .WithEvent("shuffle", 10)
                .WithRandomContainer(10, avoidRepeatCount: 1, 1, 2, 3).AsShuffle()
                .WithSound(1, Media)
                .WithSound(2, Media)
                .WithSound(3, Media)).Plan;
            var selected = new SelectedSound[16];
            NodeWalker.Select(plan, selected);

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var selection = 0; selection < 100; selection++)
                NodeWalker.Select(plan, selected);

            Assert.That(GC.GetAllocatedBytesForCurrentThread(), Is.EqualTo(allocatedBefore));
        }

        [Test]
        public void AContinuouslyValidatedLayerRampsWithoutRestartingItsVoice()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("engine", 10)
                .WithLayerContainer(10, 1)
                .WithLayerCurve("rpm", 1, (0f, -6f, 4), (100f, 0f, 4))
                .WithContinuousLayerValidation()
                .WithSound(1, MixerProbe.ConstantPcm(0.5f, 4_000));
            var output = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(output);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("car");
            engine.SetGameParameter("rpm", 0f, gameObject);
            var playingId = engine.PostEvent("engine", gameObject);
            output.Render(8);
            output.DevicePositionFrames = 8;
            var positionBeforeChange = engine.GetPosition(playingId);

            engine.SetGameParameter("rpm", 100f, gameObject);
            var ramp = output.Render(GainRamp.DeclickFrames + 8);
            output.DevicePositionFrames += GainRamp.DeclickFrames + 8;

            Assert.That(ramp[0], Is.LessThan(ramp[^1]));
            Assert.That(ramp[^1], Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(engine.GetPosition(playingId)!.Value, Is.GreaterThan(positionBeforeChange!.Value), "the existing voice kept its playhead instead of restarting");
        }

        private static (ResolvedEvent Plan, GameObjectState GameObject) Warm(
            FakeHierarchy hierarchy,
            Action<GameObjectState>? configure = null,
            string? eventName = null)
        {
            var registry = new GameObjectRegistry();
            var gameObject = registry.Find(registry.Register("test"));
            configure?.Invoke(gameObject);
            var plan = new EventProcessor(hierarchy, new NodeWalker(hierarchy), new EngineTelemetry()).Warm(
                eventName ?? (hierarchy.FindEvent("shuffle") != null ? "shuffle" : "engine"),
                gameObject);
            return (plan!, gameObject);
        }

        private static uint SelectOne(ResolvedEvent plan) => Select(plan).Single().NodeId;

        private static SelectedSound[] Select(ResolvedEvent plan)
        {
            var selected = new SelectedSound[16];
            var count = NodeWalker.Select(plan, selected);
            return [.. selected.Take(count)];
        }
    }
}
