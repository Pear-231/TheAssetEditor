using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Rendering;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;

namespace Test.Audio
{
    // Who gets to be a voice when there are not enough to go round.
    //
    // Driven at the pool rather than through the engine on purpose: priority is the input the rule
    // turns on, so handing the pool a priority directly states the case each test is about. Since
    // phase 11 the resolver reads authored priority off the hierarchy, and AuthoredParameterTests
    // covers the same rule from that end.
    public class VoicePoolTests
    {
        private static readonly TransportId Transport = new(1);

        [Test]
        public void AVoiceThatOutranksTheQuietestOne_TakesItsPlace()
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(maximumSoundingVoices: 1, telemetry);

            Activate(voices, playingIdentifier: 1, level: 0.25f, priority: 50f);
            Activate(voices, playingIdentifier: 2, level: 0.5f, priority: 60f);

            // The voice that gave way ramps out rather than being cut, so both are still audible on
            // the frame the newcomer starts on and only the newcomer is left once the ramp is over.
            var whileRampingOut = MixBlock(voices, GainRamp.DeclickFrames, firstAbsoluteOutputFrame: 0);
            var afterTheRamp = MixBlock(voices, 8, GainRamp.DeclickFrames);

            Assert.That(whileRampingOut[0], Is.EqualTo(0.75f).Within(0.25f / GainRamp.DeclickFrames), "both are still audible, the stolen one a frame into its ramp");
            Assert.That(afterTheRamp[0], Is.EqualTo(0.5f).Within(0.0001f), "the newcomer plays on alone");
            Assert.That(telemetry.StolenVoiceCount, Is.EqualTo(1));
            Assert.That(telemetry.UnstartedVoiceCount, Is.Zero);
        }

        // The rule that keeps a full pool from becoming a game of last-one-wins: a footstep must not
        // silence a death cry it happens to land on top of. It is also why nothing audible changes
        // while every sound still carries the default priority.
        [Test]
        public void AVoiceOfNoMoreImportance_DoesNotCutWhatIsAlreadySounding()
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(maximumSoundingVoices: 1, telemetry);

            Activate(voices, playingIdentifier: 1, level: 0.25f, priority: 50f);
            Activate(voices, playingIdentifier: 2, level: 0.5f, priority: 50f);
            Activate(voices, playingIdentifier: 3, level: 0.5f, priority: 20f);

            var output = MixBlock(voices, 8, firstAbsoluteOutputFrame: 0);

            Assert.That(output[0], Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(telemetry.StolenVoiceCount, Is.Zero);
            Assert.That(telemetry.UnstartedVoiceCount, Is.EqualTo(2), "both were counted rather than lost quietly");
        }

        // A node instance limit is a second budget over a subset of the pool, so a sound can be
        // turned away while the pool itself has room to spare.
        [Test]
        public void ANodeInstanceLimit_TurnsSoundsAwayWhileThePoolStillHasRoom()
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(maximumSoundingVoices: 8, telemetry);
            var limit = new VoiceLimit(NodeId: 7, MaximumInstanceCount: 2);

            for (var playingIdentifier = 1; playingIdentifier <= 4; playingIdentifier++)
                Activate(voices, playingIdentifier, level: 0.25f, priority: 50f, limit: limit);

            var output = MixBlock(voices, 8, firstAbsoluteOutputFrame: 0);

            Assert.That(output[0], Is.EqualTo(0.5f).Within(0.0001f), "two of the four sounded");
            Assert.That(telemetry.UnstartedVoiceCount, Is.EqualTo(2));
            Assert.That(telemetry.PeakSoundingVoiceCount, Is.EqualTo(2));
        }

        // Voices counted against one node do not count against another, which is what makes the
        // limit a property of the node rather than of the pool.
        [Test]
        public void ANodeInstanceLimit_CountsOnlyTheVoicesThatNodeStarted()
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(maximumSoundingVoices: 8, telemetry);

            Activate(voices, 1, 0.25f, 50f, new VoiceLimit(NodeId: 7, MaximumInstanceCount: 1));
            Activate(voices, 2, 0.25f, 50f, new VoiceLimit(NodeId: 8, MaximumInstanceCount: 1));
            Activate(voices, 3, 0.25f, 50f, new VoiceLimit(NodeId: 7, MaximumInstanceCount: 1));

            var output = MixBlock(voices, 8, firstAbsoluteOutputFrame: 0);

            Assert.That(output[0], Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(telemetry.UnstartedVoiceCount, Is.EqualTo(1), "only the second sound of node 7 was turned away");
        }

        // Nobody asked for a steal, so the post it belonged to would otherwise be waited on for a
        // sound that is never going to finish. A cancelled voice reports nothing, because the caller
        // that cancelled it already knows.
        [Test]
        public void AStolenVoice_ReportsItsPostFinished()
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(maximumSoundingVoices: 1, telemetry);
            var completion = new PostCompletionState(new PlayingId(1, Transport));

            Activate(voices, playingIdentifier: 1, level: 0.25f, priority: 50f, completion: completion);
            Activate(voices, playingIdentifier: 2, level: 0.25f, priority: 60f);
            MixBlock(voices, GainRamp.DeclickFrames + 1, firstAbsoluteOutputFrame: 0);

            Assert.That(completion.TryGetCompletion(out var outcome, out _), Is.True);
            Assert.That(outcome, Is.EqualTo(PostOutcome.Played));
        }

        [Test]
        public void AStolenVoice_RampsOutRatherThanBeingCut()
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(maximumSoundingVoices: 1, telemetry);

            Activate(voices, playingIdentifier: 1, level: 0.5f, priority: 50f);
            MixBlock(voices, 8, firstAbsoluteOutputFrame: 0);
            Activate(voices, playingIdentifier: 2, level: 0f, priority: 60f);

            // The newcomer is silent, so what is left in the block is the stolen voice on its way
            // out and nothing else.
            var rampingOut = MixBlock(voices, GainRamp.DeclickFrames, firstAbsoluteOutputFrame: 8);

            Assert.That(rampingOut[0], Is.EqualTo(0.5f).Within(0.5f / GainRamp.DeclickFrames), "it starts from where it was");
            Assert.That(MixerProbe.MaximumStep(rampingOut), Is.LessThan(1f / GainRamp.DeclickFrames), "and steps down rather than cutting");
            Assert.That(rampingOut[^1], Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void GameObjectLimitScopeCountsEachObjectSeparately()
        {
            var voices = new VoicePool(8, new EngineTelemetry());
            var limit = new VoiceLimit(7, 1, "bank.bnk", VoiceLimitScope.GameObject);

            Activate(voices, 1, 0.25f, 50f, limit, gameObject: new GameObjectId(1));
            Activate(voices, 2, 0.25f, 50f, limit, gameObject: new GameObjectId(2));

            Assert.That(MixBlock(voices, 8, 0)[0], Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void GlobalLimitScopeCountsVoicesFromEveryObjectTogether()
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(8, telemetry);
            var limit = new VoiceLimit(7, 1, "bank.bnk", VoiceLimitScope.Global);

            Activate(voices, 1, 0.25f, 50f, limit, gameObject: new GameObjectId(1));
            Activate(voices, 2, 0.5f, 50f, limit, gameObject: new GameObjectId(2));

            Assert.That(MixBlock(voices, 8, 0)[0], Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(telemetry.UnstartedVoiceCount, Is.EqualTo(1));
        }

        [Test]
        public void EqualPriorityLimitCanDiscardOldestOrNewest()
        {
            var discardOldest = new VoicePool(8, new EngineTelemetry());
            var oldestLimit = new VoiceLimit(7, 1, ReachedBehaviour: VoiceLimitReachedBehaviour.DiscardOldest);
            Activate(discardOldest, 1, 0.25f, 50f, oldestLimit);
            Activate(discardOldest, 2, 0.5f, 50f, oldestLimit);
            MixBlock(discardOldest, GainRamp.DeclickFrames, 0);
            Assert.That(MixBlock(discardOldest, 8, GainRamp.DeclickFrames)[0], Is.EqualTo(0.5f).Within(0.0001f));

            var discardNewest = new VoicePool(8, new EngineTelemetry());
            var newestLimit = new VoiceLimit(7, 1, ReachedBehaviour: VoiceLimitReachedBehaviour.DiscardNewest);
            Activate(discardNewest, 1, 0.25f, 50f, newestLimit);
            Activate(discardNewest, 2, 0.5f, 50f, newestLimit);
            Assert.That(MixBlock(discardNewest, 8, 0)[0], Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void OverLimitVoiceCanBeVirtualisedInsteadOfKilled()
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(8, telemetry);
            var limit = new VoiceLimit(
                7,
                1,
                OverLimitBehaviour: VoiceOverLimitBehaviour.Virtualise);
            Activate(voices, 1, 0.25f, 50f, limit);

            var result = Activate(voices, 2, 0.5f, 50f, limit);

            Assert.That(result, Is.EqualTo(VoiceActivationResult.Virtualised));
            Assert.That(MixBlock(voices, 8, 0)[0], Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(telemetry.UnstartedVoiceCount, Is.Zero);
        }

        [Test]
        public void SimultaneousAncestorLimitsAreEachEnforced()
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(8, telemetry);
            var parent = new VoiceLimit(10, 1, ReachedBehaviour: VoiceLimitReachedBehaviour.DiscardNewest);
            var firstChild = new VoiceLimit(20, 2);
            var secondChild = new VoiceLimit(30, 2);

            Activate(voices, 1, 0.25f, 50f, limits: new[] { parent, firstChild });
            Activate(voices, 2, 0.5f, 50f, limits: new[] { parent, secondChild });

            Assert.That(MixBlock(voices, 8, 0)[0], Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(telemetry.UnstartedVoiceCount, Is.EqualTo(1), "the shared ancestor was full although both child budgets had room");
        }

        private static VoiceActivationResult Activate(
            VoicePool voices,
            int playingIdentifier,
            float level,
            float priority,
            VoiceLimit limit = default,
            PostCompletionState? completion = null,
            GameObjectId gameObject = default,
            ReadOnlyMemory<VoiceLimit> limits = default)
        {
            var playingId = new PlayingId(playingIdentifier, Transport);
            if (limits.IsEmpty && limit.IsLimited)
                limits = new[] { limit };
            return voices.Activate(new VoiceStart(
                playingId,
                completion ?? new PostCompletionState(playingId),
                MixerProbe.ConstantPcm(level, 4_000),
                VoiceParameters.Neutral with { Priority = priority },
                limit,
                limits,
                NodeId: 0,
                gameObject,
                AncestorNodeIds: default,
                InitialMixFrame: 0,
                StartDelayFrames: 0,
                FadeInFrames: 0,
                IsLooping: false,
                IsScheduledVoice: false));
        }

        private static float[] MixBlock(VoicePool voices, int frameCount, long firstAbsoluteOutputFrame)
        {
            var bus = new Bus(frameCount);
            bus.Clear(frameCount);
            voices.MixBlock(bus, firstAbsoluteOutputFrame, frameCount);
            return bus.Samples;
        }
    }
}
