using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Moq;
using NAudio.Wave;
using Shared.Core.PackFiles;

namespace Test.Audio
{
    public class PlaybackOwnershipTests
    {
        [Test]
        public void WaveformStartedDuringAnimation_DoesNotChangeItsIdentityOrClock()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = TimelineEngine(outputDevice, out var timeline, out _);

            outputDevice.Render(32);
            outputDevice.DevicePositionFrames = 32;
            var positionBeforeWaveform = engine.GetPosition(timeline)!.Value;
            var waveform = engine.PlayMedia(Media(0.25f, 4_000));
            var output = outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 96;

            Assert.That(engine.TransportId, Is.EqualTo(timeline));
            Assert.That(waveform.TransportId, Is.Not.EqualTo(timeline));
            Assert.That(engine.GetPosition(timeline), Is.GreaterThan(positionBeforeWaveform));
            Assert.That(output[0], Is.EqualTo(0.5f).Within(0.0001f), "the timeline and waveform share the mix");
        }

        [Test]
        public void WaveformControls_LeaveAnimationAdvancingAndSounding()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = TimelineEngine(outputDevice, out var timeline, out _);
            var waveform = engine.PlayMedia(Media(0.25f, 4_000));
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 64;

            engine.Pause(waveform);
            var pausedOutput = outputDevice.Render(GainRamp.DeclickFrames + 100);
            outputDevice.DevicePositionFrames += GainRamp.DeclickFrames + 100;
            var pausedWaveformPosition = engine.GetPosition(waveform)!.Value;
            var timelineAfterPause = engine.GetPosition(timeline)!.Value;

            engine.Resume(waveform);
            outputDevice.Render(GainRamp.DeclickFrames + 20);
            outputDevice.DevicePositionFrames += GainRamp.DeclickFrames + 20;
            Assert.That(engine.GetPosition(waveform), Is.GreaterThan(pausedWaveformPosition));

            engine.Seek(waveform, TimeSpan.FromSeconds(500d / PlaybackFormat.SampleRate));
            outputDevice.Render(1);
            outputDevice.DevicePositionFrames++;
            Assert.That(engine.GetPosition(waveform), Is.EqualTo(TimeSpan.FromSeconds(501d / PlaybackFormat.SampleRate)));

            engine.StopPlayingId(waveform);
            var afterStop = outputDevice.Render(GainRamp.DeclickFrames + 20);
            outputDevice.DevicePositionFrames += GainRamp.DeclickFrames + 20;

            Assert.That(engine.GetPlaybackState(timeline), Is.EqualTo(SoundPlaybackState.Playing));
            Assert.That(engine.GetPosition(timeline), Is.GreaterThan(timelineAfterPause));
            Assert.That(pausedOutput[^1], Is.EqualTo(0.25f).Within(0.0001f), "only the timeline remains after the waveform pause ramp");
            Assert.That(afterStop[^1], Is.EqualTo(0.25f).Within(0.0001f), "stopping the waveform leaves the timeline sounding");
        }

        [Test]
        public async Task AnimationCompletion_LeavesALongerWaveformSounding()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = TimelineEngine(outputDevice, out var timeline, out _, timelineFrames: 8);
            var waveform = engine.PlayMedia(Media(0.25f, 100));
            var completed = new TaskCompletionSource<TransportId>(TaskCreationOptions.RunContinuationsAsynchronously);
            var postCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.PlaybackCompleted += completed.SetResult;
            engine.PostCompleted += playingId =>
            {
                if (playingId == waveform)
                    postCompleted.TrySetResult();
            };

            var output = outputDevice.Render(20);
            outputDevice.DevicePositionFrames = 20;
            var completedTimeline = await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.That(completedTimeline, Is.EqualTo(timeline));
            Assert.That(engine.GetPlaybackState(timeline), Is.EqualTo(SoundPlaybackState.Stopped));
            Assert.That(engine.GetPlaybackState(waveform), Is.EqualTo(SoundPlaybackState.Playing));
            Assert.That(output[^1], Is.EqualTo(0.25f).Within(0.0001f));

            outputDevice.Render(100);
            outputDevice.DevicePositionFrames = 120;
            await postCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }

        [Test]
        public void StoppingAndRebuildingAnimation_LeavesImmediatePlaybackIntact()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = TimelineEngine(outputDevice, out var firstTimeline, out _);
            var waveform = engine.PlayMedia(Media(0.25f, 4_000));
            outputDevice.Render(32);

            engine.Stop(firstTimeline);
            var afterStop = outputDevice.Render(GainRamp.DeclickFrames + 10);
            var replacementTimeline = engine.CreateTimeline(TimeSpan.FromSeconds(1), shouldLoop: false);
            var afterRebuild = outputDevice.Render(10);

            Assert.That(engine.GetPlaybackState(firstTimeline), Is.Null);
            Assert.That(engine.GetPlaybackState(replacementTimeline), Is.EqualTo(SoundPlaybackState.Stopped));
            Assert.That(engine.GetPlaybackState(waveform), Is.EqualTo(SoundPlaybackState.Playing));
            Assert.That(afterStop[^1], Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(afterRebuild[^1], Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void PreparedTimeline_StaysStoppedWhileAnotherToolPlays()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var timeline = engine.CreateTimeline(TimeSpan.FromSeconds(1), shouldLoop: false);

            var waveform = engine.PlayMedia(Media(0.25f, 100));
            var output = outputDevice.Render(10);
            outputDevice.DevicePositionFrames = 10;

            Assert.That(engine.TransportId, Is.EqualTo(timeline));
            Assert.That(engine.GetPlaybackState(timeline), Is.EqualTo(SoundPlaybackState.Stopped));
            Assert.That(engine.GetPosition(timeline), Is.EqualTo(TimeSpan.Zero));
            Assert.That(engine.GetPlaybackState(waveform), Is.EqualTo(SoundPlaybackState.Playing));
            Assert.That(output[0], Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void PausedAnimationFreezesWhileImmediatePlaybackContinues()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = TimelineEngine(outputDevice, out var timeline, out _);
            var waveform = engine.PlayMedia(Media(0.25f, 4_000));
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 64;

            engine.Pause(timeline);
            outputDevice.Render(GainRamp.DeclickFrames + 20);
            outputDevice.DevicePositionFrames += GainRamp.DeclickFrames + 20;
            var pausedTimelinePosition = engine.GetPosition(timeline);
            var waveformPosition = engine.GetPosition(waveform);

            var pausedOutput = outputDevice.Render(100);
            outputDevice.DevicePositionFrames += 100;

            Assert.That(engine.GetPosition(timeline), Is.EqualTo(pausedTimelinePosition));
            Assert.That(engine.GetPosition(waveform), Is.GreaterThan(waveformPosition!.Value));
            Assert.That(pausedOutput[^1], Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public async Task CompletedAnimationTimeline_CanStartAnotherPass()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = TimelineEngine(outputDevice, out var timeline, out _, timelineFrames: 8);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.PlaybackCompleted += completedTransportId =>
            {
                if (completedTransportId == timeline)
                    completed.TrySetResult();
            };

            outputDevice.Render(8);
            outputDevice.DevicePositionFrames = 8;
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));

            engine.Start(timeline);
            var nextPass = outputDevice.Render(1);

            Assert.That(engine.GetPlaybackState(timeline), Is.EqualTo(SoundPlaybackState.Playing));
            Assert.That(nextPass[0], Is.EqualTo(0.25f).Within(0.0001f));
        }

        private static SoundEngine TimelineEngine(
            FakeAudioOutputDevice outputDevice,
            out TransportId timeline,
            out GameObjectId gameObject,
            int timelineFrames = 4_000)
        {
            var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("timeline", 1)
                .WithSound(1, Media(0.25f, 4_000)));
            gameObject = engine.RegisterGameObject("animation");
            timeline = engine.CreateTimeline(TimeSpan.FromSeconds(timelineFrames / (double)PlaybackFormat.SampleRate), shouldLoop: false);
            engine.ScheduleEvent("timeline", gameObject, TimeSpan.Zero, timeline);
            engine.Start(timeline);
            return engine;
        }

        private static SourceMedia Media(float level, int frameCount)
        {
            using var stream = new MemoryStream();
            using (var writer = new WaveFileWriter(stream, WaveFormat.CreateIeeeFloatWaveFormat(PlaybackFormat.SampleRate, 1)))
                writer.WriteSamples(Enumerable.Repeat(level, frameCount).ToArray(), 0, frameCount);
            return new MediaCache(Mock.Of<IPackFileService>(), 1_000_000).GetWave(stream.ToArray());
        }
    }
}
