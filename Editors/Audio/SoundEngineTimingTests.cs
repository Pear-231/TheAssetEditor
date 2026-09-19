using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Commands;
using Editors.Audio.Shared.Wwise.Engine.Rendering;
using Editors.Audio.Shared.Wwise.Engine.Timing;

namespace Test.Audio
{
    // The hard constraint: audio sounds at exactly the frame the animation asked for, and the
    // animation keeps following the audio clock through start, loop, seek and pause.
    //
    // These assert the frame a cue lands on and the frame the ledger maps an output frame to.
    // Neither depends on a level, so they survive the renderer's gain staging being replaced, and
    // they are what block processing has to be measured against: firing cues on block boundaries
    // would quantise every onset here to the block size.
    public class SoundEngineTimingTests
    {
        private static readonly TransportId Transport = new(1);
        private static readonly SourceMedia Cue = MixerProbe.ConstantPcm(1f, 4);

        [Test]
        public void ScheduledCues_SoundOnTheirExactTimelineFrame_WhateverTheBufferSize()
        {
            long[] cueFrames = [1, 7, 97, 1013];

            foreach (var chunkFrames in new[] { 1, 3, 64, 100, 333, 2048 })
            {
                var renderer = new AudioRenderer();
                renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport, 2048, false));
                for (var cueIndex = 0; cueIndex < cueFrames.Length; cueIndex++)
                    renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(cueIndex + 1, Transport), default, MixerProbe.SingleSoundEvent(Cue), cueFrames[cueIndex]));
                renderer.SubmitCommand(EngineCommand.Start(Transport, 0));

                var output = MixerProbe.Render(renderer, 2048, chunkFrames);

                Assert.That(
                    MixerProbe.OnsetFrames(output),
                    Is.EqualTo(cueFrames),
                    $"cues moved when the output was rendered {chunkFrames} frames at a time");
            }
        }

        [Test]
        public void AScheduledCue_SoundsOnItsExactFrameOnEveryLoop()
        {
            const long TimelineDurationFrames = 500;
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport, TimelineDurationFrames, true));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(1, Transport), default, MixerProbe.SingleSoundEvent(Cue), 137));
            renderer.SubmitCommand(EngineCommand.Start(Transport, 0));

            var output = MixerProbe.Render(renderer, 1500, 128);

            Assert.That(MixerProbe.OnsetFrames(output), Is.EqualTo(new long[] { 137, 637, 1137 }));
        }

        [Test]
        public void StartingPartwayThroughTheTimeline_LeavesTheRemainingCuesOnTheirExactFrames()
        {
            const long StartFrame = 200;
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport, 1000, false));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(1, Transport), default, MixerProbe.SingleSoundEvent(Cue), 137));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(2, Transport), default, MixerProbe.SingleSoundEvent(Cue), 300));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(3, Transport), default, MixerProbe.SingleSoundEvent(Cue), 700));
            renderer.SubmitCommand(EngineCommand.Start(Transport, StartFrame));

            var output = MixerProbe.Render(renderer, 800, 64);

            // Rendering begins at the frame that was seeked to, so output frame k is timeline
            // frame 200 + k. The cue at 137 has already gone by and does not sound again.
            Assert.That(MixerProbe.OnsetFrames(output), Is.EqualTo(new long[] { 300 - StartFrame, 700 - StartFrame }));
        }

        [Test]
        public void EveryRenderedOutputFrame_MapsToTheTimelineFrameThatSoundedOnIt()
        {
            const int TimelineDurationFrames = 500;
            const int RenderedFrameCount = 1500;
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport, TimelineDurationFrames, true));
            renderer.SubmitCommand(EngineCommand.Start(Transport, 0));
            MixerProbe.Render(renderer, RenderedFrameCount, 128);

            var mappedTimelineFrames = new long[RenderedFrameCount];
            var expectedTimelineFrames = new long[RenderedFrameCount];
            for (var outputFrame = 0; outputFrame < RenderedFrameCount; outputFrame++)
            {
                mappedTimelineFrames[outputFrame] = renderer.TryGetTimelineFrame(outputFrame, Transport, out var timelineFrame)
                    ? timelineFrame
                    : -1;
                expectedTimelineFrames[outputFrame] = outputFrame % TimelineDurationFrames;
            }

            Assert.That(mappedTimelineFrames, Is.EqualTo(expectedTimelineFrames));
        }

        // Pause ramps out before it freezes, so the timeline runs on for the length of that ramp and
        // holds the frame it reached silence on.
        [Test]
        public void APausedTimeline_HoldsItsMappedFrameWhileTheOutputClockRuns()
        {
            const int PlayingFrameCount = 100;
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport, 5_000, false));
            renderer.SubmitCommand(EngineCommand.Start(Transport, 0));
            MixerProbe.Render(renderer, PlayingFrameCount, 50);

            renderer.SubmitCommand(EngineCommand.PauseTimeline(Transport));
            MixerProbe.Render(renderer, GainRamp.DeclickFrames + 200, 50);

            var frozenFrame = PlayingFrameCount + GainRamp.DeclickFrames;
            Assert.That(renderer.TryGetTimelineFrame(PlayingFrameCount - 1, Transport, out var lastPlayingFrame), Is.True);
            Assert.That(lastPlayingFrame, Is.EqualTo(PlayingFrameCount - 1));
            Assert.That(renderer.TryGetTimelineFrame(frozenFrame + 150, Transport, out var lastPausedFrame), Is.True);
            Assert.That(lastPausedFrame, Is.EqualTo(frozenFrame), "a paused timeline holds the frame it stopped on");
        }

        [Test]
        public void EnginePosition_ReportsTheTimelineFrameThatIsAudibleNow()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var transportId = engine.CreateTimeline(PlaybackTime.FromFrames(4_000), shouldLoop: false);
            engine.Start(transportId);
            outputDevice.Render(2_000);

            // 2,000 frames are rendered ahead; the position follows what the device has drained
            // of them, which is what keeps the animation on the audio clock rather than the
            // render clock.
            outputDevice.DevicePositionFrames = 500;
            var positionEarly = engine.Position;
            outputDevice.DevicePositionFrames = 1_500;
            var positionLate = engine.Position;

            Assert.That(positionEarly, Is.EqualTo(PlaybackTime.FromFrames(500)));
            Assert.That(positionLate, Is.EqualTo(PlaybackTime.FromFrames(1_500)));
        }

        [Test]
        public void EnginePosition_FollowsTheTimelineAroundALoop()
        {
            const int TimelineDurationFrames = 1_000;
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var transportId = engine.CreateTimeline(PlaybackTime.FromFrames(TimelineDurationFrames), shouldLoop: true);
            engine.Start(transportId);
            outputDevice.Render(2_500);

            outputDevice.DevicePositionFrames = 1_200;
            var positionOnTheSecondPass = engine.Position;
            outputDevice.DevicePositionFrames = 2_300;
            var positionOnTheThirdPass = engine.Position;

            Assert.That(positionOnTheSecondPass, Is.EqualTo(PlaybackTime.FromFrames(1_200 % TimelineDurationFrames)));
            Assert.That(positionOnTheThirdPass, Is.EqualTo(PlaybackTime.FromFrames(2_300 % TimelineDurationFrames)));
        }
    }
}
