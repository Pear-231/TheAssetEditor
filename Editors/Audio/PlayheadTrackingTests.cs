using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Media;

namespace Test.Audio
{
    // The waveform playhead is painted from GetPosition on every composition frame — about 60 times
    // a second — while buffers are rendered far less often than that. So the position has to keep
    // moving between rendered buffers, driven by the device clock, or the playhead visibly steps
    // instead of gliding.
    //
    // These drive the engine the way playback actually runs: a buffer is rendered, then the device
    // drains it in small increments, over and over. A test that renders a lot and then samples once
    // cannot see a stall, which is why the first attempts at this missed the fault entirely.
    public class PlayheadTrackingTests
    {
        // One output buffer, and the granularity the playhead is sampled at between them.
        private const int BufferFrames = 4_800;
        private const int DrainStepFrames = 480;

        // Losing the endpoint -- the default output changes, headphones are unplugged, a driver
        // restarts -- rebuilds the sink on whatever the machine is using now. The rebuilt device is
        // a new clock starting at zero, so the playhead must carry across it rather than falling
        // back to where the old device had reached.
        [Test]
        public void ThePlayhead_SurvivesTheDeviceBeingRebuiltUnderneathIt()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var post = engine.PlayMedia(LongTone());
            outputDevice.Render(BufferFrames);
            outputDevice.DevicePositionFrames = 2_400;
            var beforeLoss = engine.GetPosition(post)!.Value;

            // The endpoint goes, and the next thing that wants the device rebuilds it at zero.
            engine.Pause(post);
            outputDevice.Render(DrainStepFrames);
            outputDevice.HasLostItsEndpoint = true;
            engine.Resume(post);
            outputDevice.Render(BufferFrames);

            Assert.That(outputDevice.RebuildCount, Is.EqualTo(1), "the device was rebuilt");
            Assert.That(engine.GetPosition(post)!.Value, Is.GreaterThanOrEqualTo(beforeLoss),
                "the playhead carries across the rebuild rather than rewinding to the new clock");
        }

        // A WASAPI endpoint reports its position with a little backwards jitter -- steps of -3 and
        // -46 bytes measured on a real one. The tracker already had to cope with the device clock
        // restarting at zero, and it did that by carrying the whole previous position forward, so
        // before this was separated out a few bytes of jitter doubled the playhead.
        [Test]
        public void ThePlayhead_IgnoresBackwardsJitterFromTheDevice()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var post = engine.PlayMedia(LongTone());
            outputDevice.Render(BufferFrames);

            outputDevice.DevicePositionFrames = 2_400;
            var beforeJitter = engine.GetPosition(post)!.Value;

            // Backwards by a handful of frames, as an endpoint does.
            outputDevice.DevicePositionFrames = 2_395;
            var afterJitter = engine.GetPosition(post)!.Value;

            Assert.That(afterJitter, Is.EqualTo(beforeJitter).Within(TimeSpan.FromMilliseconds(1)),
                "a few frames backwards is jitter, and must not move the playhead");

            // Forward again, past where it was.
            outputDevice.DevicePositionFrames = 2_600;
            Assert.That(engine.GetPosition(post)!.Value, Is.GreaterThan(beforeJitter), "and the playhead keeps tracking afterwards");
        }

        // The behaviour the carry exists for, which must survive the jitter handling: a device that
        // genuinely restarts its clock at zero.
        [Test]
        public void ThePlayhead_CarriesForwardWhenTheDeviceClockRestarts()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var post = engine.PlayMedia(LongTone());
            outputDevice.Render(BufferFrames);

            outputDevice.DevicePositionFrames = 4_800;
            var beforeRestart = engine.GetPosition(post)!.Value;

            // All the way back to zero, which is a restart and not jitter.
            outputDevice.DevicePositionFrames = 0;
            Assert.That(engine.GetPosition(post)!.Value, Is.GreaterThanOrEqualTo(beforeRestart),
                "a restart carries the elapsed position forward rather than going backwards");
        }

        [Test]
        public void ThePlayhead_MovesWithTheDeviceBetweenRenderedBuffers_BeforeAndAfterAPause()
        {
            var media = LongTone();
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var post = engine.PlayMedia(media);

            // Render ahead once, as the sink does when it starts.
            outputDevice.Render(BufferFrames);

            var stallsBeforePause = LongestStall(engine, outputDevice, post, bufferCount: 4);

            engine.Pause();
            DrainAndRender(engine, outputDevice, bufferCount: 6);
            engine.Resume();
            outputDevice.Render(BufferFrames);

            var stallsAfterResume = LongestStall(engine, outputDevice, post, bufferCount: 4);

            TestContext.Out.WriteLine($"longest stall before pause: {stallsBeforePause} samples, after resume: {stallsAfterResume} samples");
            Assert.That(
                stallsAfterResume,
                Is.LessThanOrEqualTo(stallsBeforePause + 1),
                "the playhead stalls for longer after a resume than before the pause, which is seen as it stepping instead of gliding");
        }

        // The most consecutive samples over which the reported position did not move at all.
        private static int LongestStall(SoundEngine engine, FakeAudioOutputDevice outputDevice, PlayingId post, int bufferCount)
        {
            var longestStall = 0;
            var currentStall = 0;
            var previousPosition = engine.GetPosition(post);

            for (var buffer = 0; buffer < bufferCount; buffer++)
            {
                for (var drainStep = 0; drainStep < BufferFrames / DrainStepFrames; drainStep++)
                {
                    outputDevice.DevicePositionFrames += DrainStepFrames;
                    var position = engine.GetPosition(post);

                    if (position == previousPosition)
                        currentStall++;
                    else
                        currentStall = 0;

                    longestStall = Math.Max(longestStall, currentStall);
                    previousPosition = position;
                }

                outputDevice.Render(BufferFrames);
            }

            return longestStall;
        }

        private static void DrainAndRender(SoundEngine engine, FakeAudioOutputDevice outputDevice, int bufferCount)
        {
            for (var buffer = 0; buffer < bufferCount; buffer++)
            {
                outputDevice.Render(BufferFrames);
                outputDevice.DevicePositionFrames += BufferFrames;
            }
        }

        // Long enough that nothing under test runs off the end of it.
        private static SourceMedia LongTone()
        {
            const int FrameCount = 600_000;
            var samples = new float[FrameCount * PlaybackFormat.ChannelCount];
            for (var frame = 0; frame < FrameCount; frame++)
            {
                var value = 0.25f * MathF.Sin(frame * 0.01f);
                for (var channel = 0; channel < PlaybackFormat.ChannelCount; channel++)
                    samples[frame * PlaybackFormat.ChannelCount + channel] = value;
            }
            return MixerProbe.ToMedia(samples);
        }
    }
}
