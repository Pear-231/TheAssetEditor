using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Mixing;

namespace Test.Audio
{
    // Container resolution: what an event turns into, and how that changes from one pass to the
    // next.
    //
    // The first three are the behaviours the old single-leaf resolver did not have, and each was
    // deliberately left broken until there was a walker to fix it in. They are this phase's
    // acceptance criteria rather than incidental cover.
    public class NodeWalkerTests
    {
        private static readonly SourceMedia Media = MixerProbe.ConstantPcm(0.25f, 16);

        // A missing first variation used to silence the whole event, because the traversal took the
        // container's first child and nothing else.
        [Test]
        public void ARandomContainer_ResolvesAgainstItsWholePlaylist_NotItsFirstChild()
        {
            var plan = Warm(
                new FakeHierarchy()
                    .WithEvent("foley", 10)
                    .WithRandomContainer(10, avoidRepeatCount: 1, 1, 2, 3)
                    .WithSound(1, Media)
                    .WithSound(2, Media)
                    .WithSound(3, Media),
                "foley");

            var soundedNodeIds = new HashSet<uint>();
            for (var pass = 0; pass < 30; pass++)
                soundedNodeIds.UnionWith(Select(plan));

            Assert.That(soundedNodeIds, Is.EquivalentTo(new uint[] { 1, 2, 3 }));
        }

        [Test]
        public void ARandomContainer_WhoseFirstVariationHasNoMedia_StillSounds()
        {
            var plan = Warm(
                new FakeHierarchy()
                    .WithEvent("foley", 10)
                    .WithRandomContainer(10, avoidRepeatCount: 0, 1, 2, 3)
                    .WithSound(1, null)
                    .WithSound(2, Media)
                    .WithSound(3, Media),
                "foley");

            for (var pass = 0; pass < 30; pass++)
                Assert.That(
                    Select(plan),
                    Has.Length.EqualTo(1),
                    "a variation with no media silenced the event instead of costing a variation");
        }

        // Returning a set rather than one sound is what this is for: the old traversal returned on
        // the first child that resolved, so only one layer ever sounded.
        [Test]
        public void ALayerContainer_SoundsEveryLayer()
        {
            var plan = Warm(
                new FakeHierarchy()
                    .WithEvent("impact", 10)
                    .WithLayerContainer(10, 1, 2, 3)
                    .WithSound(1, Media)
                    .WithSound(2, Media)
                    .WithSound(3, Media),
                "impact");

            Assert.That(Select(plan), Is.EqualTo(new uint[] { 1, 2, 3 }));
        }

        // A visited set shared across the whole walk is the natural way to write a recursive walker
        // and is wrong here. One sound under two layers has to sound twice.
        [Test]
        public void ASoundReachedByTwoRoutes_SoundsOnBoth()
        {
            var plan = Warm(
                new FakeHierarchy()
                    .WithEvent("impact", 10)
                    .WithLayerContainer(10, 11, 12)
                    .WithLayerContainer(11, 1)
                    .WithLayerContainer(12, 1)
                    .WithSound(1, Media),
                "impact");

            Assert.That(Select(plan), Is.EqualTo(new uint[] { 1, 1 }));
        }

        // Capping the walk covers a cyclic graph; it is a separate requirement from the one above,
        // and this is the half that must not hang.
        [Test]
        public void AContainerThatReachesItself_StopsAndStillSoundsWhatItCan()
        {
            var plan = Warm(
                new FakeHierarchy()
                    .WithEvent("cyclic", 10)
                    .WithLayerContainer(10, 1, 11)
                    .WithLayerContainer(11, 10)
                    .WithSound(1, Media),
                "cyclic");

            Assert.That(Select(plan), Is.EqualTo(new uint[] { 1 }));
        }

        [Test]
        public void ASequenceContainer_WalksItsPlaylistInOrderAndWrapsAround()
        {
            var plan = Warm(
                new FakeHierarchy()
                    .WithEvent("footsteps", 10)
                    .WithSequenceContainer(10, 1, 2, 3)
                    .WithSound(1, Media)
                    .WithSound(2, Media)
                    .WithSound(3, Media),
                "footsteps");

            var soundedNodeIds = new List<uint>();
            for (var pass = 0; pass < 4; pass++)
                soundedNodeIds.AddRange(Select(plan));

            Assert.That(soundedNodeIds, Is.EqualTo(new uint[] { 1, 2, 3, 1 }));
        }

        [Test]
        public void ASwitchContainer_WarmsTheBranchTheGameObjectIsIn()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("melee", 10)
                .WithSwitchContainer(
                    10,
                    "Generic_Melee_Weapon_Type",
                    defaultSwitchValueName: "creatures_claws",
                    ("creatures_claws", [1]),
                    ("sword", [2]))
                .WithSound(1, Media)
                .WithSound(2, Media);

            Assert.That(Select(Warm(hierarchy, "melee")), Is.EqualTo(new uint[] { 1 }), "the container's own default stands until the game says otherwise");
            Assert.That(
                Select(Warm(hierarchy, "melee", gameObject => gameObject.SetSwitch("Generic_Melee_Weapon_Type", "sword"))),
                Is.EqualTo(new uint[] { 2 }));
        }

        // Actor mixers provide inherited settings but Wwise does not make them playable targets.
        [Test]
        public void AnActorMixer_DoesNotInventAChildSelectionRule()
        {
            var plan = Warm(
                new FakeHierarchy()
                    .WithEvent("mixed", 10)
                    .WithActorMixer(10, 1, 2)
                    .WithSound(1, null)
                    .WithSound(2, Media),
                "mixed");

            Assert.That(Select(plan), Is.Empty);
        }

        // An event with several Play actions starts all of them, which is why the plan holds a set
        // of roots rather than one.
        [Test]
        public void AnEventWithSeveralPlayActions_SoundsEachOfThem()
        {
            var plan = Warm(
                new FakeHierarchy()
                    .WithEvent("burst", 1, 2)
                    .WithSound(1, Media)
                    .WithSound(2, Media),
                "burst");

            Assert.That(Select(plan), Is.EqualTo(new uint[] { 1, 2 }));
        }

        // An instance limit is authored on a node and governs everything beneath it, so the sound
        // that eventually becomes a voice has to carry its container's limit rather than its own —
        // the audio thread cannot walk back up the tree to find it.
        [Test]
        public void AnInstanceLimitOnAContainer_ReachesEverySoundBeneathIt()
        {
            var plan = Warm(
                new FakeHierarchy()
                    .WithEvent("foley", 10)
                    .WithLayerContainer(10, 1, 2).LimitedTo(3)
                    .WithSound(1, Media)
                    .WithSound(2, Media),
                "foley");

            Assert.That(SelectedLimits(plan).Select(limit => (limit.NodeId, limit.MaximumInstanceCount)),
                Is.EqualTo(new[] { (10u, 3), (10u, 3) }));
        }

        [Test]
        public void EveryAncestorInstanceLimit_ReachesTheSoundIndependently()
        {
            var plan = Warm(
                new FakeHierarchy()
                    .WithEvent("foley", 20)
                    .WithActorMixer(10, 20).LimitedTo(8)
                    .WithLayerContainer(20, 1).LimitedTo(2).Under(10)
                    .WithSound(1, Media),
                "foley");

            var limits = SelectedSounds(plan).Single().Limits.ToArray();
            Assert.That(limits.Select(limit => (limit.NodeId, limit.MaximumInstanceCount)),
                Is.EqualTo(new[] { (10u, 8), (20u, 2) }));
        }

        [Test]
        public void ASoundWithNoLimitAnywhereAboveIt_IsNotLimited()
        {
            var plan = Warm(
                new FakeHierarchy()
                    .WithEvent("foley", 10)
                    .WithLayerContainer(10, 1)
                    .WithSound(1, Media),
                "foley");

            Assert.That(SelectedLimits(plan), Is.EqualTo(new[] { VoiceLimit.None }));
        }

        [Test]
        public void AnEventThatIsNotInAnyBank_WarmsToNothing()
        {
            Assert.That(Warm(new FakeHierarchy(), "missing"), Is.Null);
        }

        private static ResolvedEvent? Warm(FakeHierarchy hierarchy, string eventName, Action<GameObjectState>? setUpGameObject = null)
        {
            var gameObjects = new GameObjectRegistry();
            var gameObject = gameObjects.Find(gameObjects.Register("test"));
            setUpGameObject?.Invoke(gameObject);
            return new EventProcessor(hierarchy, new NodeWalker(hierarchy), new EngineTelemetry()).Warm(eventName, gameObject);
        }

        private static uint[] Select(ResolvedEvent? plan)
            => SelectedSounds(plan).Select(selectedSound => selectedSound.NodeId).ToArray();

        private static VoiceLimit[] SelectedLimits(ResolvedEvent? plan)
            => SelectedSounds(plan).Select(selectedSound => selectedSound.Limit).ToArray();

        private static SelectedSound[] SelectedSounds(ResolvedEvent? plan)
        {
            var selectedSounds = new SelectedSound[32];
            var selectedCount = NodeWalker.Select(plan, selectedSounds);
            return [.. selectedSounds.Take(selectedCount)];
        }
    }
}
