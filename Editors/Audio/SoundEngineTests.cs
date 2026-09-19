using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Editors.Audio.Shared.Wwise.Engine.Commands;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Rendering;
using Moq;
using NAudio.Wave;
using Shared.Core.PackFiles;

namespace Test.Audio
{
    public class SoundEngineTests
    {
        [Test]
        public void ScheduledVoices_StartOnExactSampleFrame_AndOverlap()
        {
            var renderer = new AudioRenderer(maximumVoices: 4);
            var first = MixerProbe.ConstantPcm(0.25f, 4);
            var second = MixerProbe.ConstantPcm(0.25f, 4);
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport(1), 8, false));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(1, Transport(1)), default, MixerProbe.SingleSoundEvent(first), 0));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(2, Transport(1)), default, MixerProbe.SingleSoundEvent(second), 2));
            renderer.SubmitCommand(EngineCommand.Start(Transport(1), 0));

            var output = new float[12];
            renderer.Render(output, 0, output.Length);

            Assert.That(output[0], Is.EqualTo(0.25f).Within(0.0001));
            Assert.That(output[2], Is.EqualTo(0.25f).Within(0.0001));
            Assert.That(output[4], Is.EqualTo(0.5f).Within(0.0001), "the overlap sums, and is left alone below the limiter's ceiling");
            Assert.That(output[8], Is.EqualTo(0.25f).Within(0.0001));
        }

        // Pause ramps out over a few milliseconds rather than cutting, so the timeline runs on for
        // the length of that ramp and then freezes for as long as the pause lasts.
        [Test]
        public void Pause_FreezesTheTimelineAndTheVoicesOnceItHasRampedOut()
        {
            const int TimelineDurationFrames = 4_000;
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport(3), TimelineDurationFrames, false));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(1, Transport(3)), default, MixerProbe.SingleSoundEvent(MixerProbe.ConstantPcm(0.5f, TimelineDurationFrames)), 0));
            renderer.SubmitCommand(EngineCommand.Start(Transport(3), 0));
            MixerProbe.Render(renderer, 100, 50);

            renderer.SubmitCommand(EngineCommand.PauseTimeline(Transport(3)));
            MixerProbe.Render(renderer, GainRamp.DeclickFrames, 64);
            var frozenPlaybackPosition = renderer.PlaybackPositionFrames;
            var whilePaused = MixerProbe.Render(renderer, 200, 64);

            Assert.That(frozenPlaybackPosition, Is.EqualTo(100 + GainRamp.DeclickFrames), "the ramp plays out before the transport freezes");
            Assert.That(renderer.PlaybackPositionFrames, Is.EqualTo(frozenPlaybackPosition), "and then it does not move");
            Assert.That(whilePaused, Is.All.EqualTo(0f));
            Assert.That(
                renderer.RenderedOutputFrameCount,
                Is.EqualTo(300 + GainRamp.DeclickFrames),
                "the absolute output clock advances while the timeline is paused");
        }

        [Test]
        public void Pause_RampsDownRatherThanCutting()
        {
            var renderer = new AudioRenderer();
            var playingId = new PlayingId(1, Transport(4));
            renderer.SubmitCommand(EngineCommand.PlayMedia(playingId, default, MixerProbe.ConstantPcm(0.5f, 4_000), 0, false));
            MixerProbe.Render(renderer, 100, 50);

            renderer.SubmitCommand(EngineCommand.PausePlayingId(playingId));
            var rampingOut = MixerProbe.Render(renderer, GainRamp.DeclickFrames, 64);

            Assert.That(rampingOut[0], Is.EqualTo(0.5f).Within(0.5f / GainRamp.DeclickFrames), "the ramp starts from where the audio was");
            Assert.That(MixerProbe.MaximumStep(rampingOut), Is.LessThan(1f / GainRamp.DeclickFrames), "and gets there a step at a time");
            Assert.That(rampingOut[^1], Is.EqualTo(0f).Within(0.01f), "arriving at silence by the end of it");
        }

        // A voice put somewhere new by the transport ramps in from the frame it was moved to, so the
        // audible value is only its own once the ramp is behind it.
        [Test]
        public void ImmediatePlayback_PauseSeekResume_ContinuesFromTheSeekedFrame()
        {
            // Long enough to outlast the seek cross-fade this test renders through.
            const int SourceFrameCount = 4_000;
            const int SeekFrame = 100;
            var renderer = new AudioRenderer();
            var samples = Enumerable.Range(0, SourceFrameCount)
                .SelectMany(frame => new[] { frame / (float)SourceFrameCount, frame / (float)SourceFrameCount })
                .ToArray();
            var playingId = new PlayingId(1, Transport(5));
            renderer.SubmitCommand(EngineCommand.PlayMedia(playingId, default, MixerProbe.ToMedia(samples), 0, false));
            MixerProbe.Render(renderer, 10, 10);

            renderer.SubmitCommand(EngineCommand.PausePlayingId(playingId));
            renderer.SubmitCommand(EngineCommand.SeekPlayingId(playingId, SeekFrame));
            renderer.ProcessPendingCommandsWithoutRendering();
            renderer.SubmitCommand(EngineCommand.ResumePlayingId(playingId, 0));

            // The seek cross-fade, not the de-click ramp: a seek replaces the voice and the
            // replacement fades in over the longer span.
            MixerProbe.Render(renderer, GainRamp.SeekCrossFadeFrames, 64);
            var resumed = MixerProbe.Render(renderer, 1, 1);

            var resumedFrame = SeekFrame + GainRamp.SeekCrossFadeFrames;
            Assert.That(renderer.TryGetPostPosition(playingId, null, out var playbackPosition), Is.True);
            Assert.That(playbackPosition, Is.EqualTo(resumedFrame + 1));
            Assert.That(resumed[0], Is.EqualTo(resumedFrame / (float)SourceFrameCount).Within(0.0001f));
            Assert.That(resumed[1], Is.EqualTo(resumedFrame / (float)SourceFrameCount).Within(0.0001f));
        }

        // Reconstructing a cue part way through is a transport discontinuity, so the voice ramps in
        // rather than appearing at full level mid-waveform.
        [Test]
        public void Seek_ReconstructsAnInProgressScheduledVoice()
        {
            // Long enough to outlast the seek cross-fade this test renders through.
            const int SourceFrameCount = 4_000;
            const int CueFrame = 3;
            const int StartFrame = 200;
            var renderer = new AudioRenderer();
            var samples = Enumerable.Range(0, SourceFrameCount)
                .SelectMany(frame => new[] { frame / (float)SourceFrameCount, frame / (float)SourceFrameCount })
                .ToArray();
            // The timeline has to outlast the seek cross-fade as well as the media does, or it
            // completes part way through and the voice is correctly silent.
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport(5), 10_000, false));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(1, Transport(5)), default, MixerProbe.SingleSoundEvent(MixerProbe.ToMedia(samples)), CueFrame));
            renderer.SubmitCommand(EngineCommand.Start(Transport(5), StartFrame));

            // Rendered past the seek cross-fade, not the de-click ramp: a Start at a non-zero
            // position reconstructs its voices the way a seek does, and that fade is the longer one.
            var output = MixerProbe.Render(renderer, GainRamp.SeekCrossFadeFrames + 1, 64);

            var reconstructedFrame = StartFrame - CueFrame + GainRamp.SeekCrossFadeFrames;
            Assert.That(output[^2], Is.EqualTo(reconstructedFrame / (float)SourceFrameCount).Within(0.0001));
        }

        [Test]
        public void Loop_ReplaysSchedulesEveryTimelineCycle()
        {
            var renderer = new AudioRenderer();
            var pcmSamples = MixerProbe.ConstantPcm(0.5f, 1);
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport(7), 3, true));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(1, Transport(7)), default, MixerProbe.SingleSoundEvent(pcmSamples), 1));
            renderer.SubmitCommand(EngineCommand.Start(Transport(7), 0));

            var output = new float[14];
            renderer.Render(output, 0, output.Length);

            Assert.That(output[2], Is.EqualTo(0.5f).Within(0.0001));
            Assert.That(output[8], Is.EqualTo(0.5f).Within(0.0001));
        }

        [Test]
        public void OutputTimelineLedger_MapsDeviceFramesAcrossLoopBoundary()
        {
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport(8), 3, true));
            renderer.SubmitCommand(EngineCommand.Start(Transport(8), 0));
            renderer.Render(new float[10], 0, 10);

            Assert.That(renderer.TryGetTimelineFrame(0, Transport(8), out var frame0), Is.True);
            Assert.That(frame0, Is.Zero);
            Assert.That(renderer.TryGetTimelineFrame(2, Transport(8), out var frame2), Is.True);
            Assert.That(frame2, Is.EqualTo(2));
            Assert.That(renderer.TryGetTimelineFrame(3, Transport(8), out var wrappedFrame), Is.True);
            Assert.That(wrappedFrame, Is.Zero);
            Assert.That(renderer.TryGetTimelineFrame(4, Transport(8), out var nextFrame), Is.True);
            Assert.That(nextFrame, Is.EqualTo(1));
            Assert.That(renderer.TryGetTimelineFrame(3, Transport(99), out _), Is.False, "a stale transport cannot drive animation");
        }

        // The ledger going on publishing the frozen frame is what lets the playhead settle onto it
        // by itself, instead of the position having to be seeked to somewhere on the pause.
        [Test]
        public void OutputTimelineLedger_HoldsTheFrozenFrameWhilePaused()
        {
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport(10), 4_000, false));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(1, Transport(10)), default, MixerProbe.SingleSoundEvent(MixerProbe.ConstantPcm(1f, 4_000)), 0));
            renderer.SubmitCommand(EngineCommand.Start(Transport(10), 0));
            MixerProbe.Render(renderer, 100, 50);
            renderer.SubmitCommand(EngineCommand.PauseTimeline(Transport(10)));
            MixerProbe.Render(renderer, GainRamp.DeclickFrames + 200, 64);

            var frozenTimelineFrame = 100 + GainRamp.DeclickFrames;
            Assert.That(renderer.TryGetTimelineFrame(frozenTimelineFrame, Transport(10), out var firstFrozenFrame), Is.True);
            Assert.That(firstFrozenFrame, Is.EqualTo(frozenTimelineFrame));
            Assert.That(renderer.TryGetTimelineFrame(frozenTimelineFrame + 150, Transport(10), out var laterFrozenFrame), Is.True);
            Assert.That(laterFrozenFrame, Is.EqualTo(frozenTimelineFrame), "the frame does not move for as long as the pause lasts");
        }

        [Test]
        public void TimelineCompletion_RecordsFirstDeviceFrameAfterFinalAudio()
        {
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport(12), 3, false));
            renderer.SubmitCommand(EngineCommand.Start(Transport(12), 0));
            renderer.Render(new float[6], 0, 6);

            Assert.That(renderer.CompletedTransportId, Is.EqualTo(Transport(12)));
            Assert.That(renderer.CompletedOutputFrame, Is.EqualTo(3));
        }

        // Every command names the transport it was meant for, which is what a replaced timeline
        // leaves behind: commands for it are inert rather than landing on the timeline that took
        // its place.
        [Test]
        public void CommandsForAReplacedTransport_DoNothing()
        {
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport(1), 1_000, false));
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport(2), 1_000, false));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(
                new PlayingId(1, Transport(1)),
                default,
                MixerProbe.SingleSoundEvent(MixerProbe.ConstantPcm(0.5f, 64)),
                0));
            renderer.SubmitCommand(EngineCommand.Start(Transport(1), 0));

            var output = MixerProbe.Render(renderer, 64, 64);

            Assert.That(output, Is.All.EqualTo(0f), "the replaced transport neither started nor sounded its cue");
            Assert.That(renderer.PlaybackPositionFrames, Is.Zero, "and the transport that replaced it stayed where it was");
            Assert.That(
                renderer.Telemetry.UnscheduledEventCount,
                Is.Zero,
                "a schedule for a timeline that is gone is dropped rather than counted as a shortfall");
        }

        [Test]
        public void VoiceLimit_DropsNewVoiceWithoutCuttingExistingVoice()
        {
            var renderer = new AudioRenderer(maximumVoices: 1);
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport(9), 10, false));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(1, Transport(9)), default, MixerProbe.SingleSoundEvent(MixerProbe.ConstantPcm(0.25f, 5)), 0));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(2, Transport(9)), default, MixerProbe.SingleSoundEvent(MixerProbe.ConstantPcm(1f, 5)), 0));
            renderer.SubmitCommand(EngineCommand.Start(Transport(9), 0));
            var output = new float[2];
            renderer.Render(output, 0, output.Length);
            Assert.That(output[0], Is.EqualTo(0.25f).Within(0.0001));
        }

        // The whole way through, from an authored limit on a container to what comes out of the
        // mix: a layer container of three sounds that is only allowed two instances sounds two.
        [Test]
        public void AnAuthoredInstanceLimit_CapsWhatIsHeard()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("impact", 10)
                .WithLayerContainer(10, 1, 2, 3).LimitedTo(2)
                .WithSound(1, MixerProbe.ConstantPcm(0.25f, 4_000))
                .WithSound(2, MixerProbe.ConstantPcm(0.25f, 4_000))
                .WithSound(3, MixerProbe.ConstantPcm(0.25f, 4_000));

            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            engine.PostEvent("impact", engine.RegisterGameObject("test"));

            var output = outputDevice.Render(GainRamp.DeclickFrames + 8);
            Assert.That(output[^1], Is.EqualTo(0.5f).Within(0.0001f), "discard-oldest left two layers sounding after its de-click ramp");
        }

        // Direct media carries no authored priority — there is no node above it to state one — so
        // every voice here is equally important and the pool behaves as it did before voices could be
        // stolen at all: what is already playing is left alone and what could not start is counted.
        [Test]
        public void WithNoPriorityToSeparateThem_AFullPoolTurnsNewSoundsAwayRatherThanCuttingOldOnes()
        {
            var renderer = new AudioRenderer(maximumVoices: 2);
            for (var playingIdentifier = 1; playingIdentifier <= 5; playingIdentifier++)
                renderer.SubmitCommand(EngineCommand.PlayMedia(
                    new PlayingId(playingIdentifier, Transport(30)),
                    default,
                    MixerProbe.ConstantPcm(0.25f, 4_000),
                    0,
                    false));

            var output = MixerProbe.Render(renderer, 8, 8);

            Assert.That(output[0], Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(renderer.Telemetry.StolenVoiceCount, Is.Zero);
            Assert.That(renderer.Telemetry.UnstartedVoiceCount, Is.EqualTo(3));
            Assert.That(renderer.Telemetry.PeakSoundingVoiceCount, Is.EqualTo(2));
        }

        // The level the mix asked for, not the level the limiter allowed out, which is the only
        // one that says whether the limiter is working or merely present.
        [Test]
        public void Telemetry_RecordsWhatTheMixAskedForBeforeLimiting()
        {
            var renderer = new AudioRenderer(maximumVoices: 4);
            for (var playingIdentifier = 1; playingIdentifier <= 3; playingIdentifier++)
                renderer.SubmitCommand(EngineCommand.PlayMedia(
                    new PlayingId(playingIdentifier, Transport(31)),
                    default,
                    MixerProbe.ConstantPcm(0.5f, 4_000),
                    0,
                    false));

            var output = MixerProbe.Render(renderer, 64, 64);

            Assert.That(renderer.Telemetry.MasterPeakLevel, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(MixerProbe.PeakAmplitude(output), Is.LessThan(1f), "and the limiter held it below the rail");
        }

        [Test]
        public void ImmediateVoices_OverlapWithoutReplacingEachOther()
        {
            var renderer = new AudioRenderer(maximumVoices: 4);
            renderer.SubmitCommand(EngineCommand.PlayMedia(new PlayingId(1, Transport(11)), default, MixerProbe.ConstantPcm(0.25f, 4), 0, false));
            renderer.SubmitCommand(EngineCommand.PlayMedia(new PlayingId(2, Transport(11)), default, MixerProbe.ConstantPcm(0.25f, 4), 0, false));
            var output = new float[2];
            renderer.Render(output, 0, output.Length);
            Assert.That(output[0], Is.EqualTo(0.5f).Within(0.0001));
        }

        [Test]
        public void CancellingOneVoice_LeavesTheOtherVoicesPlaying()
        {
            var renderer = new AudioRenderer(maximumVoices: 4);
            renderer.SubmitCommand(EngineCommand.PlayMedia(new PlayingId(1, Transport(20)), default, MixerProbe.ConstantPcm(0.25f, 4_000), 0, false));
            renderer.SubmitCommand(EngineCommand.PlayMedia(new PlayingId(2, Transport(20)), default, MixerProbe.ConstantPcm(0.25f, 4_000), 0, false));
            renderer.Render(new float[2], 0, 2);

            renderer.SubmitCommand(EngineCommand.StopPlayingId(new PlayingId(1, Transport(20))));
            var output = MixerProbe.Render(renderer, GainRamp.DeclickFrames + 1, 64);

            Assert.That(output[^2], Is.EqualTo(0.25f).Within(0.0001), "the surviving voice keeps playing once the cancelled one has ramped out");
        }

        // Media is held as authored. Converting it at load is what made rate conversion and pitch
        // mutually exclusive, so the cache now decodes and stops.
        [Test]
        public void Cache_KeepsTheSourceRateAndChannelCount()
        {
            var media = CreateCache(1_000_000).GetWave(
                WaveBytes([0f, 0.25f, 0.5f, 0.75f], sampleRate: 24_000));

            Assert.That(media.SampleRate, Is.EqualTo(24_000));
            Assert.That(media.ChannelCount, Is.EqualTo(1), "a mono source is not widened to the mix's channel count");
            Assert.That(media.Samples.Length, Is.EqualTo(4), "nor resampled to the mix's rate");
            Assert.That(media.Duration, Is.EqualTo(TimeSpan.FromSeconds(4d / 24_000)));
        }

        [Test]
        public void Rendering_DoesNotAllocateAfterCommandsAreApplied()
        {
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.PlayMedia(new PlayingId(1, Transport(14)), default, MixerProbe.ConstantPcm(0.25f, 10_000), 0, true));
            var output = new float[512];
            for (var iteration = 0; iteration < 100; iteration++)
                renderer.Render(output, 0, output.Length);

            var allocatedBytesBeforeRendering = GC.GetAllocatedBytesForCurrentThread();
            for (var iteration = 0; iteration < 100; iteration++)
                renderer.Render(output, 0, output.Length);
            var allocatedBytesAfterRendering = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(allocatedBytesAfterRendering, Is.EqualTo(allocatedBytesBeforeRendering));
        }

        // Steady-state rendering allocating nothing is no longer the whole story: from this phase
        // on, firing a cue walks the hierarchy plan and picks a container child inside the render
        // callback. A GC pause landing mid-buffer is the thing that actually produces a glitch, so
        // the loop below fires cues out of random and layer containers again and again.
        [Test]
        public void FiringAScheduledCue_DoesNotAllocate()
        {
            const long TimelineDurationFrames = 480;
            var hierarchy = new FakeHierarchy()
                .WithEvent("foley", 10)
                .WithRandomContainer(10, avoidRepeatCount: 1, 1, 2, 3)
                .WithEvent("impact", 20)
                .WithLayerContainer(20, 4, 5)
                .WithSound(1, MixerProbe.ConstantPcm(0.1f, 200))
                .WithSound(2, MixerProbe.ConstantPcm(0.1f, 200))
                .WithSound(3, MixerProbe.ConstantPcm(0.1f, 200))
                .WithSound(4, MixerProbe.ConstantPcm(0.1f, 200))
                .WithSound(5, MixerProbe.ConstantPcm(0.1f, 200));
            var gameObjects = new GameObjectRegistry();
            var gameObject = gameObjects.Find(gameObjects.Register("test"));
            var eventProcessor = new EventProcessor(hierarchy, new NodeWalker(hierarchy), new EngineTelemetry());

            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport(1), TimelineDurationFrames, true));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(1, Transport(1)), default, eventProcessor.Warm("foley", gameObject), 7));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(2, Transport(1)), default, eventProcessor.Warm("impact", gameObject), 251));
            renderer.SubmitCommand(EngineCommand.Start(Transport(1), 0));

            var output = new float[512];
            for (var iteration = 0; iteration < 100; iteration++)
                renderer.Render(output, 0, output.Length);

            var allocatedBytesBeforeRendering = GC.GetAllocatedBytesForCurrentThread();
            for (var iteration = 0; iteration < 100; iteration++)
                renderer.Render(output, 0, output.Length);
            var allocatedBytesAfterRendering = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(allocatedBytesAfterRendering, Is.EqualTo(allocatedBytesBeforeRendering));
        }

        [Test]
        public void ACompletedPost_IsReportedToTheControlSide()
        {
            var renderer = new AudioRenderer();
            var playingId = new PlayingId(41, Transport(13));
            var completion = new PostCompletionState(playingId);
            renderer.SubmitCommand(EngineCommand.PlayMedia(playingId, default, MixerProbe.ConstantPcm(1f, 1), 0, false, completion));

            renderer.Render(new float[2], 0, 2);
            Assert.That(completion.IsCompleted, Is.False);

            renderer.Render(new float[2], 0, 2);
            Assert.That(completion.TryGetCompletion(out var outcome, out _), Is.True);
            Assert.That(outcome, Is.EqualTo(PostOutcome.Played));
        }

        // A layer container starts several voices under one playing id, and the post is only over
        // when the last of them is: reporting on the first would tell the caller a sound had
        // finished while it was still audible.
        [Test]
        public void APostOfSeveralVoices_IsReportedCompleteOnlyWhenTheLongestEnds()
        {
            var renderer = new AudioRenderer();
            var playingId = new PlayingId(7, Transport(21));
            var completion = new PostCompletionState(playingId);
            var builder = new ResolvedEventBuilder();
            var layerIndices = new List<int>
            {
                builder.AddSound(1, 1, MixerProbe.ConstantPcm(0.25f, 2), VoiceLimit.None),
                builder.AddSound(2, 2, MixerProbe.ConstantPcm(0.25f, 8), VoiceLimit.None)
            };
            builder.AddRoot(builder.AddContainer(ResolvedNodeKind.All, 3, null, layerIndices, null));
            renderer.SubmitCommand(EngineCommand.PostEvent(playingId, default, builder.Build("layered"), completion));

            MixerProbe.Render(renderer, 4, 1);
            Assert.That(completion.IsCompleted, Is.False, "the short layer finishing does not finish the post");

            MixerProbe.Render(renderer, 6, 1);
            Assert.That(completion.TryGetCompletion(out var outcome, out _), Is.True);
            Assert.That(outcome, Is.EqualTo(PostOutcome.Played));
        }

        [Test]
        public async Task Cache_DeduplicatesConcurrentContent_AndSharesOneEntry()
        {
            var cache = CreateCache(1_000_000);
            var wave = WaveBytes(Enumerable.Repeat(0.25f, 100).ToArray());
            var tasks = Enumerable.Range(0, 16)
                .Select(_ => Task.Run(() => cache.GetWave(wave)))
                .ToArray();

            var cachedMedia = await Task.WhenAll(tasks);
            Assert.That(cache.CachedMediaCount, Is.EqualTo(1));
            Assert.That(cachedMedia.Select(media => media.ContentHash).Distinct().Count(), Is.EqualTo(1));
            Assert.That(cachedMedia.Distinct().Count(), Is.EqualTo(1));
        }

        // The resampling and the mono-to-stereo placement that used to happen at load still have to
        // happen — they moved into the voice. A source at half the mix rate must still come out at
        // the right pitch and length, on both channels.
        [Test]
        public void AVoiceResamplesAndPlacesASourceThatIsNotInTheMixFormat()
        {
            const int SourceSampleRate = 24_000;
            var monoSamples = Enumerable.Range(0, 200).Select(frame => frame / 200f).ToArray();
            var media = MixerProbe.ToMedia(monoSamples, channelCount: 1, sampleRate: SourceSampleRate);
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.PlayMedia(new PlayingId(1, Transport(30)), default, media, 0, false));

            var output = MixerProbe.Render(renderer, 200, 64);

            // Two output frames per source frame, so output frame 2n is source frame n exactly and
            // the odd frames sit half way between neighbours.
            Assert.That(output[0], Is.EqualTo(monoSamples[0]).Within(0.0001f));
            Assert.That(output[2 * 20], Is.EqualTo(monoSamples[10]).Within(0.0001f));
            Assert.That(output[2 * 21], Is.EqualTo((monoSamples[10] + monoSamples[11]) / 2f).Within(0.0001f));
            for (var frame = 0; frame < 100; frame++)
                Assert.That(
                    output[frame * 2],
                    Is.EqualTo(output[frame * 2 + 1]).Within(0.0001f),
                    "a mono source is placed on both output channels");
        }

        [Test]
        public void Cache_EvictsLeastRecentlyUsedEntry()
        {
            // Mono float at 48 kHz, so each of these is 400 bytes now that nothing is widened to
            // stereo at load.
            var cache = CreateCache(900);
            var firstWave = WaveBytes(Enumerable.Repeat(0.1f, 100).ToArray());
            var secondWave = WaveBytes(Enumerable.Repeat(0.2f, 100).ToArray());
            var thirdWave = WaveBytes(Enumerable.Repeat(0.3f, 100).ToArray());

            var first = cache.GetWave(firstWave);
            var second = cache.GetWave(secondWave);

            var recentlyUsedFirst = cache.GetWave(firstWave);
            var third = cache.GetWave(thirdWave);

            Assert.That(cache.CachedBytes, Is.LessThanOrEqualTo(cache.CapacityBytes));
            Assert.That(cache.CachedMediaCount, Is.EqualTo(2));

            var reacquiredFirst = cache.GetWave(firstWave);
            Assert.That(cache.CachedMediaCount, Is.EqualTo(2), "the recently used first entry remained cached");
        }

        [Test]
        public void SoundEngine_ReusesInjectedOutputDeviceAcrossImmediatePlayback()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 100).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            engine.PlayMedia(audio);
            engine.PlayMedia(audio);

            Assert.That(outputDevice.CreationCount, Is.EqualTo(1));
            Assert.That(outputDevice.EnsurePlayingCount, Is.EqualTo(2));
        }

        [Test]
        public void SoundEngine_ConcurrentPostsOwnIndependentPlaybackIdentities()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 100).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var firstVoice = engine.PlayMedia(audio);
            var secondVoice = engine.PlayMedia(audio);

            Assert.That(secondVoice.TransportId, Is.Not.EqualTo(firstVoice.TransportId));
            Assert.That(secondVoice.Value, Is.Not.EqualTo(firstVoice.Value));
        }

        [Test]
        public async Task SoundEngine_ReportsCompletionOfThePostThatFinished()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes([0.25f, 0.25f]));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var completedVoiceSource = new TaskCompletionSource<PlayingId>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.PostCompleted += completedVoiceSource.SetResult;

            var voice = engine.PlayMedia(audio);
            outputDevice.Render(3);
            outputDevice.DevicePositionFrames = 3;

            var completedVoice = await completedVoiceSource.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.That(completedVoice, Is.EqualTo(voice));
        }

        // A device left running with nothing to render repeats the buffer it last rendered whenever
        // a callback is late, which used to be heard as a burst of buzzing over the audio that was
        // just cancelled — and the engine worked around it by stopping the device. Ramping the
        // voice out instead leaves silence in that buffer, so there is nothing left to repeat.
        [Test]
        public void SoundEngine_CancellingTheLastAudibleVoice_RampsItOutAndLeavesTheDeviceRunning()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var voice = engine.PlayMedia(audio);
            outputDevice.Render(64);
            engine.StopPlayingId(voice);
            var rampingOut = outputDevice.Render(GainRamp.DeclickFrames);
            var afterTheRamp = outputDevice.Render(64);

            Assert.That(outputDevice.IsPlaying, Is.True, "the output device is no longer torn down to cover a cut voice");
            Assert.That(MixerProbe.MaximumStep(rampingOut), Is.LessThan(2f / GainRamp.DeclickFrames), "the voice steps down rather than cutting");
            Assert.That(afterTheRamp, Is.All.EqualTo(0f), "and is gone once the ramp finishes");
        }

        [Test]
        public void SoundEngine_CancellingOneOfSeveralVoices_LeavesTheOthersSounding()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var cancelledVoice = engine.PlayMedia(audio);
            var survivingVoice = engine.PlayMedia(audio);
            outputDevice.Render(64);
            engine.StopPlayingId(cancelledVoice);
            var afterTheRamp = outputDevice.Render(GainRamp.DeclickFrames + 64);

            Assert.That(engine.GetPlaybackState(survivingVoice), Is.EqualTo(SoundPlaybackState.Playing));
            Assert.That(afterTheRamp[^1], Is.EqualTo(0.25f).Within(0.0001f), "the surviving voice is untouched");
        }

        [Test]
        public void SoundEngine_CancellingAVoiceWhileATimelineExists_LeavesTheTimelineIntact()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var timelineTransport = engine.CreateTimeline(TimeSpan.FromSeconds(1), shouldLoop: false);
            engine.Start(timelineTransport);
            var voice = engine.PlayMedia(audio);
            outputDevice.Render(64);
            engine.StopPlayingId(voice);

            Assert.That(engine.TransportId, Is.EqualTo(timelineTransport));
        }

        [Test]
        public void SoundEngine_PositionUsesInjectedDevicePosition()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 100).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var voice = engine.PlayMedia(audio);
            outputDevice.Render(10);
            outputDevice.DevicePositionFrames = 4;

            Assert.That(engine.GetPosition(voice), Is.EqualTo(TimeSpan.FromSeconds(4d / PlaybackFormat.SampleRate)));
        }

        // The waveform playhead paints from GetPosition, so a resume that reported a position
        // behind the one shown at the pause would visibly rewind the filled part of the graph.
        // Not rewinding is not the same as tracking. The waveform playhead is painted from this on
        // every composition frame, so between one render callback and the next it has to keep
        // moving with the device clock. If it falls back to reporting how far the renderer has
        // read, it only moves when a buffer is rendered — seen as a playhead that jumps a bufferful
        // at a time instead of gliding.
        [Test]
        public void SoundEngine_AfterResuming_ThePlayheadKeepsMovingBetweenRenderedBuffers()
        {
            var cache = CreateCache(4_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 200_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var post = engine.PlayMedia(audio);
            outputDevice.Render(8_000);
            outputDevice.DevicePositionFrames = 1_000;

            // The device is never stopped, so it goes on draining across the pause while the voice
            // does not. Those frames are not part of the sound and must not be counted against it.
            // A pause lasts as long as it takes somebody to press play again — two seconds here.
            // The renderer keeps being called throughout, because the device keeps consuming; it
            // just produces silence, so the device drains 96,000 frames that the voice never
            // advanced through.
            engine.Pause(post);
            outputDevice.Render(96_000);
            outputDevice.DevicePositionFrames = 97_000;

            engine.Resume(post);
            outputDevice.Render(4_000);

            // No rendering from here: only the device drains, exactly as it does between callbacks.
            var positionAtResume = engine.GetPosition(post);
            outputDevice.DevicePositionFrames += 500;
            var positionAfterDraining = engine.GetPosition(post);

            Assert.That(positionAtResume, Is.Not.Null);
            Assert.That(positionAfterDraining, Is.Not.Null);
            var advancedBy = positionAfterDraining!.Value - positionAtResume!.Value;
            Assert.That(
                advancedBy.TotalSeconds,
                Is.EqualTo(500d / PlaybackFormat.SampleRate).Within(0.0005),
                "the playhead stopped following the device and is only moving when a buffer is rendered");
        }

        [Test]
        public void SoundEngine_ResumeAfterPause_DoesNotRewindTheReportedPosition()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var voice = engine.PlayMedia(audio);
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 40;
            var positionBeforePause = engine.GetPosition(voice);

            engine.Pause(voice);
            // Output frames the physical device still drains while it is being stopped: the
            // absolute output clock advances over these, the timeline and the voices do not.
            outputDevice.Render(16);
            var positionWhilePaused = engine.GetPosition(voice);

            engine.Resume(voice);
            var positionAtResume = engine.GetPosition(voice);
            outputDevice.Render(64);
            var positionAfterResumedBuffer = engine.GetPosition(voice);

            Assert.That(positionBeforePause, Is.EqualTo(TimeSpan.FromSeconds(40d / PlaybackFormat.SampleRate)));
            Assert.That(positionWhilePaused, Is.EqualTo(positionBeforePause));
            Assert.That(positionAtResume, Is.EqualTo(positionBeforePause));
            Assert.That(positionAfterResumedBuffer, Is.EqualTo(positionBeforePause), "the device clock has not advanced, so neither has the playhead");
        }

        // WasapiOut stops asynchronously and resets its clock to zero when the client is torn
        // down, so the reset can land after playback has already resumed. The playhead must not
        // fall back to the pause position when it does.
        [Test]
        public void SoundEngine_DeviceClockRestartingAfterResume_DoesNotRewindTheReportedPosition()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var voice = engine.PlayMedia(audio);
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 40;

            engine.Pause(voice);
            engine.Resume(voice);
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 48;
            var positionBeforeClockRestart = engine.GetPosition(voice);

            outputDevice.DevicePositionFrames = 0;
            var positionAfterClockRestart = engine.GetPosition(voice);
            outputDevice.DevicePositionFrames = 5;
            var positionAfterClockAdvanced = engine.GetPosition(voice);

            Assert.That(positionBeforeClockRestart, Is.EqualTo(TimeSpan.FromSeconds(48d / PlaybackFormat.SampleRate)));
            Assert.That(positionAfterClockRestart, Is.EqualTo(positionBeforeClockRestart), "a device clock reset must not move the audible position");
            Assert.That(positionAfterClockAdvanced, Is.EqualTo(TimeSpan.FromSeconds(53d / PlaybackFormat.SampleRate)));
        }

        // The device is no longer stopped on a pause, so it goes on consuming buffers and its clock
        // goes on running for as long as the pause lasts. The playhead has to stop at the frame the
        // transport froze on and stay there, rather than following the clock.
        [Test]
        public void SoundEngine_DeviceClockRunningThroughPause_DoesNotAdvanceThePlayhead()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 40_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var voice = engine.PlayMedia(audio);
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 40;
            var positionBeforePause = engine.GetPosition(voice);

            // The pause ramp plays out, and then the device drains a long way past it — which is
            // where the playhead would run away to if it were only counting output frames.
            engine.Pause(voice);
            outputDevice.Render(GainRamp.DeclickFrames + 2_000);
            outputDevice.DevicePositionFrames = 1_500;
            var positionWhilePaused = engine.GetPosition(voice);
            outputDevice.DevicePositionFrames = 2_000;
            var positionLaterInTheSamePause = engine.GetPosition(voice);

            Assert.That(positionBeforePause, Is.EqualTo(TimeSpan.FromSeconds(40d / PlaybackFormat.SampleRate)));
            Assert.That(
                positionWhilePaused,
                Is.EqualTo(TimeSpan.FromSeconds((64 + GainRamp.DeclickFrames) / (double)PlaybackFormat.SampleRate)),
                "the playhead settles on the frame the transport ramped down to");
            Assert.That(positionLaterInTheSamePause, Is.EqualTo(positionWhilePaused), "and stays there however long the pause lasts");
        }

        // Selecting a second file in the explorer cancels the first voice and plays another. The
        // new one has to report its own position from its own start rather than inheriting where
        // the previous file had got to.
        [Test]
        public void SoundEngine_PlayingAfterAnotherVoiceWasCancelled_StartsTheNewVoiceAtZero()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 40_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var firstVoice = engine.PlayMedia(audio);
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 40;
            engine.StopPlayingId(firstVoice);
            outputDevice.Render(GainRamp.DeclickFrames);

            var renderedFramesBeforeTheSecondFile = 64 + GainRamp.DeclickFrames;
            var secondVoice = engine.PlayMedia(audio);
            outputDevice.Render(1_000);

            // The device reaches the frame the second file started on, so nothing of it has been
            // heard yet.
            outputDevice.DevicePositionFrames = renderedFramesBeforeTheSecondFile;
            Assert.That(engine.GetPosition(secondVoice), Is.EqualTo(TimeSpan.Zero), "a newly started file begins at zero");

            outputDevice.DevicePositionFrames = renderedFramesBeforeTheSecondFile + 500;
            Assert.That(
                engine.GetPosition(secondVoice),
                Is.EqualTo(TimeSpan.FromSeconds(500d / PlaybackFormat.SampleRate)),
                "and then advances with the device, not with the whole session");
        }

        // The same on the timeline path Super View drives: stopping one animation and starting
        // another must not carry the first one's position into the second.
        [Test]
        public void SoundEngine_StartingATimelineAfterAnotherWasStopped_StartsAtZero()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 40_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(new FakeHierarchy().WithEvent("cue", 1).WithSound(1, audio));
            var previewedUnit = engine.RegisterGameObject("test");

            var firstTransport = engine.CreateTimeline(TimeSpan.FromSeconds(1), shouldLoop: false);
            engine.ScheduleEvent("cue", previewedUnit, TimeSpan.Zero, firstTransport);
            engine.Start(firstTransport);
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 40;
            engine.Stop();

            var renderedFramesBeforeTheSecondTimeline = 64;
            var secondTransport = engine.CreateTimeline(TimeSpan.FromSeconds(1), shouldLoop: false);
            engine.ScheduleEvent("cue", previewedUnit, TimeSpan.Zero, secondTransport);
            engine.Start(secondTransport);
            outputDevice.Render(1_000);

            outputDevice.DevicePositionFrames = renderedFramesBeforeTheSecondTimeline;
            Assert.That(engine.Position, Is.EqualTo(TimeSpan.Zero), "a newly started timeline begins at zero");

            outputDevice.DevicePositionFrames = renderedFramesBeforeTheSecondTimeline + 500;
            Assert.That(
                engine.Position,
                Is.EqualTo(TimeSpan.FromSeconds(500d / PlaybackFormat.SampleRate)),
                "and then advances with the device");
        }

        // A cue is warmed against the switch values the game object held when it was scheduled, so
        // a switch changing afterwards leaves the timeline holding a branch the unit is no longer
        // in. The engine sends the walk round again rather than waiting to be rescheduled.
        [Test]
        public void ChangingASwitch_RewarmsWhatIsAlreadyScheduled()
        {
            Assert.That(SoundHeardFromSwitchContainer(switchValueName: null), Is.EqualTo(0.25f).Within(0.0001f), "the container's own default");
            Assert.That(SoundHeardFromSwitchContainer(switchValueName: "sword"), Is.EqualTo(0.5f).Within(0.0001f));
        }

        private static float SoundHeardFromSwitchContainer(string? switchValueName)
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("melee", 10)
                .WithSwitchContainer(
                    10,
                    "Generic_Melee_Weapon_Type",
                    defaultSwitchValueName: "creatures_claws",
                    ("creatures_claws", [1]),
                    ("sword", [2]))
                .WithSound(1, MixerProbe.ConstantPcm(0.25f, 4_000))
                .WithSound(2, MixerProbe.ConstantPcm(0.5f, 4_000));

            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            var previewedUnit = engine.RegisterGameObject("test");

            var transportId = engine.CreateTimeline(TimeSpan.FromSeconds(1), shouldLoop: false);
            engine.ScheduleEvent("melee", previewedUnit, TimeSpan.Zero, transportId);
            if (switchValueName != null)
                engine.SetSwitch("Generic_Melee_Weapon_Type", switchValueName, previewedUnit);
            engine.Start(transportId);

            return outputDevice.Render(64)[0];
        }

        // Transport ids are handed out by the engine, so a test driving the renderer directly
        // makes its own — a distinct one per test, so a command from one cannot be mistaken for
        // a command from another.
        private static TransportId Transport(long identifier) => new(identifier);

        // A container has no event above it in the audio explorer tree, and Wwise has no runtime
        // verb for playing one. It enters the upper engine at the node instead, which is the same
        // walk an event reaches one level further down.
        [Test]
        public void ANodeWithNoEventAboveIt_SoundsThroughPlayNode()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithLayerContainer(10, 1, 2)
                .WithSound(1, MixerProbe.ConstantPcm(0.25f, 4_000))
                .WithSound(2, MixerProbe.ConstantPcm(0.25f, 4_000)));

            var playingId = engine.PlayNode(10, engine.RegisterGameObject("explorer"));

            Assert.That(playingId, Is.Not.EqualTo(default(PlayingId)));
            Assert.That(outputDevice.Render(8)[0], Is.EqualTo(0.5f).Within(0.0001f), "both layers sounded");
        }

        // The visible difference from previewing a file: the explorer auditions a container the
        // way the game would sound it, so pressing play again picks again.
        [Test]
        public void ARandomContainerPlayedAsANode_PicksAFreshVariationEachTime()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithRandomContainer(10, avoidRepeatCount: 1, 1, 2, 3)
                .WithSound(1, MixerProbe.ConstantPcm(0.1f, 4))
                .WithSound(2, MixerProbe.ConstantPcm(0.2f, 4))
                .WithSound(3, MixerProbe.ConstantPcm(0.3f, 4)));
            var gameObject = engine.RegisterGameObject("explorer");

            var heardLevels = new HashSet<float>();
            for (var press = 0; press < 30; press++)
            {
                engine.PlayNode(10, gameObject);
                heardLevels.Add(MathF.Round(outputDevice.Render(4)[0], 3));
            }

            Assert.That(heardLevels, Is.EquivalentTo(new[] { 0.1f, 0.2f, 0.3f }));
        }

        // An event reached by id rather than by name is the same event. The audio explorer picks
        // one out of the hierarchy and never learns what Super View calls it.
        [Test]
        public void AnEventPostedById_SoundsWhatTheSameEventPostedByNameWould()
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("impact", 10)
                .WithSound(10, MixerProbe.ConstantPcm(0.25f, 4_000));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("explorer");

            var byName = engine.PostEvent("impact", gameObject);
            var byId = engine.PostEvent(hierarchy.FindEvent("impact")!.Id, gameObject);

            Assert.That(byName, Is.Not.EqualTo(default(PlayingId)));
            Assert.That(byId, Is.Not.EqualTo(default(PlayingId)));
            Assert.That(outputDevice.Render(8)[0], Is.EqualTo(0.5f).Within(0.0001f), "both posts sounded");
        }

        // The three entry points differ in what the caller is holding and in nothing else. A tool
        // never has to know which one produced a sound in order to stop it or to find it again.
        [Test]
        public void EveryEntryPoint_ProducesAPostThatBehavesTheSame()
        {
            var cache = CreateCache(1_000_000);
            var media = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("impact", 10)
                .WithSound(10, MixerProbe.ConstantPcm(0.25f, 4_000))
                .WithSound(20, MixerProbe.ConstantPcm(0.25f, 4_000)));
            var gameObject = engine.RegisterGameObject("explorer");

            var posts = new[]
            {
                engine.PostEvent("impact", gameObject),
                engine.PlayNode(20, gameObject),
                engine.PlayMedia(media)
            };
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 32;

            Assert.Multiple(() =>
            {
                foreach (var post in posts)
                {
                    Assert.That(post, Is.Not.EqualTo(default(PlayingId)), "every entry point returns a playing id");
                    Assert.That(engine.GetPosition(post), Is.Not.Null, "and a position that can be read back");
                }
            });

            foreach (var post in posts)
                engine.StopPlayingId(post);
            var afterTheRamps = outputDevice.Render(GainRamp.DeclickFrames + 64);

            Assert.That(afterTheRamps[^1], Is.EqualTo(0f).Within(0.0001f), "and is stoppable by that id alone");
        }

        // Direct media is not a hole in the parameter pipeline: it plays on a game object of the
        // engine own making, so the pool, the telemetry and the parameter resolver all see it as
        // an ordinary voice rather than a special case beside them.
        [Test]
        public void APlayMediaVoice_IsCountedLikeAnyOther()
        {
            var cache = CreateCache(1_000_000);
            var media = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            engine.PlayMedia(media);
            engine.PlayMedia(media);
            outputDevice.Render(8);

            Assert.That(engine.Telemetry.PeakSoundingVoiceCount, Is.EqualTo(2));
        }

        private static MediaCache CreateCache(long capacityBytes)
            => new(Mock.Of<IPackFileService>(), capacityBytes);

        private static byte[] WaveBytes(float[] monoSamples, int sampleRate = 48_000)
        {
            using var stream = new MemoryStream();
            using (var writer = new WaveFileWriter(stream, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1)))
                writer.WriteSamples(monoSamples, 0, monoSamples.Length);
            return stream.ToArray();
        }
    }
}
