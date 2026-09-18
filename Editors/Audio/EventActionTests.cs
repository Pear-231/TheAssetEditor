using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Test.Audio
{
    // An event is a list of actions, and until phase 11 the engine carried out one of them. These
    // are the rest: a delay that moves a start, a fade that is the author's rather than the
    // transport's, and the actions that reach what is already sounding instead of starting more.
    public class EventActionTests
    {
        private const int FramesPerMillisecond = PlaybackFormat.SampleRate / 1000;

        // Quiet enough that two of them together stay under the master limiter's ceiling: this is
        // measuring what the actions did, not what the limiter did about it.
        private static SourceMedia Tone() => MixerProbe.ConstantPcm(0.25f, 48_000);

        // The delay lands on the frame it authored, not on the block boundary after it.
        [Test]
        public void ADelayedPlayAction_StartsOnTheFrameItAuthored()
        {
            var hierarchy = new FakeHierarchy()
                .WithActionEvent("impact", (AkActionType.Play, 1u, 10, 0))
                .WithSound(1, Tone());

            var output = PostAndRender(hierarchy, "impact", frameCount: 1_000);
            var delayFrames = 10 * FramesPerMillisecond;

            Assert.That(output[(delayFrames - 2) * PlaybackFormat.ChannelCount], Is.Zero, "the sound started before its delay was up");
            Assert.That(output[delayFrames * PlaybackFormat.ChannelCount], Is.EqualTo(0.25f).Within(0.0001f));
        }

        // An authored fade is a level change the author wrote, so it is the voice's whole opening
        // rather than the 3 ms the transport uses to hide a discontinuity.
        [Test]
        public void AFadeInAction_ReachesFullGainOverTheTimeItAuthored()
        {
            var hierarchy = new FakeHierarchy()
                .WithActionEvent("swell", (AkActionType.Play, 1u, 0, 20))
                .WithSound(1, Tone());

            var output = PostAndRender(hierarchy, "swell", frameCount: 2_000);
            var fadeFrames = 20 * FramesPerMillisecond;

            Assert.That(AmplitudeAt(output, 0), Is.LessThan(0.01f), "the fade opened at full gain");
            Assert.That(AmplitudeAt(output, fadeFrames / 2), Is.EqualTo(0.125f).Within(0.01f), "the fade was not half way at its half way point");
            Assert.That(AmplitudeAt(output, fadeFrames + 10), Is.EqualTo(0.25f).Within(0.0001f));
        }

        // A stop action reaches what the node it names has sounding, and the fade it authored is how
        // long that takes.
        [Test]
        public void AStopAction_SilencesWhatItsTargetHasSounding()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("start", 1)
                .WithActionEvent("stop", (AkActionType.Stop_E_O, 1u, 0, 0))
                .WithSound(1, Tone());

            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("test");

            engine.PostEvent("start", gameObject);
            Assert.That(MixerProbe.PeakAmplitude(outputDevice.Render(500)), Is.EqualTo(0.25f).Within(0.0001f));

            engine.PostEvent("stop", gameObject);
            outputDevice.Render(500);
            Assert.That(MixerProbe.PeakAmplitude(outputDevice.Render(500)), Is.Zero, "the stop action left the sound playing");
        }

        // The "_O" variants reach only the emitter the event was posted on, which is the difference
        // between one unit falling silent and every unit falling silent.
        [Test]
        public void AStopActionScopedToAGameObject_LeavesAnotherObjectsVoicesSounding()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("start", 1)
                .WithActionEvent("stop", (AkActionType.Stop_E_O, 1u, 0, 0))
                .WithSound(1, Tone());

            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            var stoppedObject = engine.RegisterGameObject("stopped");
            var untouchedObject = engine.RegisterGameObject("untouched");

            engine.PostEvent("start", stoppedObject);
            engine.PostEvent("start", untouchedObject);
            Assert.That(MixerProbe.PeakAmplitude(outputDevice.Render(500)), Is.EqualTo(0.5f).Within(0.0001f), "both objects should be sounding");

            engine.PostEvent("stop", stoppedObject);
            outputDevice.Render(500);
            Assert.That(
                MixerProbe.PeakAmplitude(outputDevice.Render(500)),
                Is.EqualTo(0.25f).Within(0.0001f),
                "the stop reached the other game object as well");
        }

        // A stop that names a container reaches the sounds underneath it, which the audio thread can
        // only answer because warming wrote the path down.
        [Test]
        public void AStopActionTargetingAContainer_ReachesTheSoundsBeneathIt()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("start", 10)
                .WithActionEvent("stop", (AkActionType.Stop_E_O, 10u, 0, 0))
                .WithLayerContainer(10, 1, 2)
                .WithSound(1, Tone())
                .WithSound(2, Tone());

            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("test");

            engine.PostEvent("start", gameObject);
            Assert.That(MixerProbe.PeakAmplitude(outputDevice.Render(500)), Is.EqualTo(0.5f).Within(0.0001f));

            engine.PostEvent("stop", gameObject);
            outputDevice.Render(500);
            Assert.That(MixerProbe.PeakAmplitude(outputDevice.Render(500)), Is.Zero, "a layer survived a stop aimed at its container");
        }

        [Test]
        public void APauseActionAndAResumeAction_FreezeAndRestartWhatTheTargetHasSounding()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("start", 1)
                .WithActionEvent("pause", (AkActionType.Pause_E_O, 1u, 0, 0))
                .WithActionEvent("resume", (AkActionType.Resume_E_O, 1u, 0, 0))
                .WithSound(1, Tone());

            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("test");

            engine.PostEvent("start", gameObject);
            outputDevice.Render(500);

            engine.PostEvent("pause", gameObject);
            outputDevice.Render(500);
            Assert.That(MixerProbe.PeakAmplitude(outputDevice.Render(500)), Is.Zero, "the pause action left the sound playing");

            engine.PostEvent("resume", gameObject);
            outputDevice.Render(500);
            Assert.That(MixerProbe.PeakAmplitude(outputDevice.Render(500)), Is.EqualTo(0.25f).Within(0.0001f), "the resume action did not bring it back");
        }

        // An action this engine does not carry out is counted and named rather than passed over in
        // silence, because an unimplemented action and a missing sound look identical from outside.
        [Test]
        public void AnActionTypeThisEngineDoesNotCarryOut_IsCounted()
        {
            var hierarchy = new FakeHierarchy()
                .WithActionEvent("mixed", (AkActionType.Play, 1u, 0, 0), (AkActionType.SetVolume_O, 1u, 0, 0))
                .WithSound(1, Tone());

            var telemetry = new EngineTelemetry();
            var gameObjects = new GameObjectRegistry();
            var gameObject = gameObjects.Find(gameObjects.Register("test"));
            var plan = new EventProcessor(hierarchy, new NodeWalker(hierarchy), telemetry).Warm("mixed", gameObject);

            Assert.That(telemetry.UnsupportedActionCount, Is.EqualTo(1));
            Assert.That(plan!.Actions, Has.Length.EqualTo(1), "the unsupported action was carried out anyway");
        }

        // A SetSwitch action cannot move the registry from the audio thread, so it hands the change
        // to the control thread. What warming does with it in the meantime is warm the rest of the
        // event against the branch the action selects, so the media is resident before the cue.
        [Test]
        public void ASetSwitchAction_WarmsTheBranchItSelectsAndHandsTheChangeOver()
        {
            var hierarchy = new FakeHierarchy()
                .WithActionEvent("swap", (AkActionType.SetSwitch, 0u, 0, 0), (AkActionType.Play, 10u, 0, 0))
                .WithSwitchContainer(10, "surface", "grass", ("grass", [1]), ("stone", [2]))
                .WithSound(1, Tone())
                .WithSound(2, Tone());
            hierarchy.WithSwitchOnAction("swap", "surface", "stone");

            var gameObjects = new GameObjectRegistry();
            var gameObject = gameObjects.Find(gameObjects.Register("test"));
            var plan = new EventProcessor(hierarchy, new NodeWalker(hierarchy), new EngineTelemetry()).Warm("swap", gameObject);

            Assert.That(plan!.Transitions, Has.Length.EqualTo(1));
            Assert.That(plan.Transitions[0].GroupType, Is.EqualTo(AkGroupType.Switch));
            Assert.That(plan.Transitions[0].ValueName, Is.EqualTo("stone"));
            Assert.That(
                SelectedNodeIds(plan),
                Is.EqualTo(new uint[] { 2 }),
                "the play action after the switch was warmed against the old branch");
        }

        // The queue between the two threads: published inside the render callback, taken on the
        // control thread's poll, and never grown while the audio thread is holding it.
        [Test]
        public void APendingTransition_CrossesFromTheRenderThreadToTheControlThread()
        {
            var queue = new PendingTransitionQueue(capacity: 2);
            var transition = new ResolvedTransition(AkGroupType.State, "combat", "active");

            Assert.That(queue.TryPublish(transition), Is.True);
            Assert.That(queue.TryPublish(transition), Is.True);
            Assert.That(queue.TryPublish(transition), Is.False, "a full queue grew instead of refusing");

            Assert.That(queue.TryTake(out var taken), Is.True);
            Assert.That(taken, Is.EqualTo(transition));
            Assert.That(queue.TryTake(out _), Is.True);
            Assert.That(queue.TryTake(out _), Is.False);
        }


        // A container can switch on a state group rather than a switch group, and then the value
        // comes from the game rather than from the emitter. That is what states are for, and it is
        // the reason they live on the registry rather than on a game object.
        [Test]
        public void AContainerThatSwitchesOnAState_ResolvesAgainstTheGamesValueRatherThanTheObjects()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("ambience", 10)
                .WithStateContainer(10, "weather", "clear", ("clear", [1]), ("storm", [2]))
                .WithSound(1, Tone())
                .WithSound(2, Tone());

            var gameObjects = new GameObjectRegistry();
            var gameObject = gameObjects.Find(gameObjects.Register("test"));
            var eventProcessor = new EventProcessor(hierarchy, new NodeWalker(hierarchy), new EngineTelemetry());

            Assert.That(SelectedNodeIds(eventProcessor.Warm("ambience", gameObject)!), Is.EqualTo(new uint[] { 1 }));

            gameObjects.SetState("weather", "storm");
            Assert.That(SelectedNodeIds(eventProcessor.Warm("ambience", gameObject)!), Is.EqualTo(new uint[] { 2 }));
        }

        // A state is the game's, so setting it once reaches every emitter rather than the one that
        // happened to be named.
        [Test]
        public void AStateIsTheGamesRatherThanOneEmittersAndReachesEveryObject()
        {
            var gameObjects = new GameObjectRegistry();
            var firstObject = gameObjects.Find(gameObjects.Register("first"));
            var secondObject = gameObjects.Find(gameObjects.Register("second"));

            Assert.That(gameObjects.SetState("weather", "storm"), Is.True);
            Assert.That(gameObjects.SetState("weather", "storm"), Is.False, "setting a state to what it already is reported a change");

            Assert.That(firstObject.TryGetGroupValue(AkGroupType.State, "weather", out var firstValue), Is.True);
            Assert.That(secondObject.TryGetGroupValue(AkGroupType.State, "weather", out var secondValue), Is.True);
            Assert.That(firstValue, Is.EqualTo("storm"));
            Assert.That(secondValue, Is.EqualTo("storm"));

            // A switch is the emitter's, and setting one on an object leaves the other alone.
            Assert.That(firstObject.SetSwitch("surface", "stone"), Is.True);
            Assert.That(secondObject.TryGetGroupValue(AkGroupType.Switch, "surface", out _), Is.False);
        }

        // A game parameter is per emitter, and what the render thread reads is a snapshot published
        // whole rather than the dictionary the control thread is editing.
        [Test]
        public void AGameParameter_IsPublishedAsAnImmutableSnapshot()
        {
            var gameObjects = new GameObjectRegistry();
            var gameObject = gameObjects.Find(gameObjects.Register("test"));

            Assert.That(gameObject.PublishedParameters.Count, Is.Zero);

            gameObject.SetGameParameter("distance", 12f);
            var firstSnapshot = gameObject.PublishedParameters;
            Assert.That(firstSnapshot.TryGetGameParameter("distance", out var distance), Is.True);
            Assert.That(distance, Is.EqualTo(12f));

            gameObject.SetGameParameter("distance", 30f);
            Assert.That(gameObject.PublishedParameters, Is.Not.SameAs(firstSnapshot), "the snapshot was edited instead of replaced");
            Assert.That(firstSnapshot.TryGetGameParameter("distance", out var unchangedDistance), Is.True);
            Assert.That(unchangedDistance, Is.EqualTo(12f), "a snapshot the render thread may be holding changed underneath it");
        }

        private static uint[] SelectedNodeIds(ResolvedEvent plan)
        {
            var selectedSounds = new SelectedSound[8];
            var selectedCount = NodeWalker.Select(plan, selectedSounds);
            return selectedSounds.Take(selectedCount).Select(selectedSound => selectedSound.NodeId).ToArray();
        }

        private static float AmplitudeAt(float[] output, int frame)
            => MathF.Abs(output[frame * PlaybackFormat.ChannelCount]);

        private static float[] PostAndRender(FakeHierarchy hierarchy, string eventName, int frameCount)
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            engine.PostEvent(eventName, engine.RegisterGameObject("test"));
            return outputDevice.Render(frameCount);
        }
    }
}
