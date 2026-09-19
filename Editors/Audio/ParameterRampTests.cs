using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;

namespace Test.Audio
{
    // Retargeting a sounding voice moves its authored properties instead of restarting it. Volume
    // was the only one that moved: the filter cutoffs stepped at the next block boundary and pitch
    // did not move at all, because nothing pushed the new value at the resampler.
    //
    // These drive the mechanism rather than an authored scenario. Nothing in a bank currently
    // retargets a voice to a different pitch — layer curves move volume only — so an authored test
    // would pass whether or not the pitch arrived, which is exactly how it went unnoticed.
    public class ParameterRampTests
    {
        private static readonly TransportId Transport = new(1);
        private const int SourceFrameCount = 4_000;

        [Test]
        public void ARampReachesItsTargetOnTheFrameItWasGiven_AndNotBefore()
        {
            var ramp = new ParameterRamp();
            ramp.SetImmediately(0f);
            ramp.RampTo(100f, 200);

            Assert.That(ramp.Advance(50), Is.EqualTo(25f).Within(0.001f));
            Assert.That(ramp.Advance(100), Is.EqualTo(75f).Within(0.001f));
            Assert.That(ramp.IsRamping, Is.True);
            Assert.That(ramp.Advance(50), Is.EqualTo(100f).Within(0.001f));
            Assert.That(ramp.IsRamping, Is.False);
        }

        // A block longer than the ramp lands on the target rather than overshooting it, which is
        // what would happen if the step were simply multiplied by the whole block.
        [Test]
        public void ARampDoesNotOvershootWhenABlockOutlastsIt()
        {
            var ramp = new ParameterRamp();
            ramp.SetImmediately(-1_200f);
            ramp.RampTo(0f, 144);

            Assert.That(ramp.Advance(512), Is.EqualTo(0f).Within(0.001f));
            Assert.That(ramp.Advance(512), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void ARetargetedVoiceReachesItsNewPitchGradually()
        {
            var media = MixerProbe.ToMedia(SourceFramePositionSignal());
            var voice = new Voice(new EngineTelemetry());
            var playingId = new PlayingId(1, Transport);
            voice.Activate(
                VoiceStart.ForMedia(playingId, new PostCompletionState(playingId), media, VoiceParameters.Neutral, 0, isLooping: false),
                startOrdinal: 0);

            // The signal reads out the source frame the voice is sitting on, so the advance across
            // a block is what the pitch did to it.
            var atUnityPitch = AdvanceAcrossBlock(voice, GainRamp.DeclickFrames);
            Assert.That(atUnityPitch, Is.EqualTo(GainRamp.DeclickFrames).Within(1f));

            var retargeted = voice.TryRetarget(
                playingId,
                nodeId: 0,
                media,
                VoiceParameters.Neutral with { PitchCents = 1_200f },
                fadeInFrames: 0,
                fadeOutFrames: 0,
                generation: 1,
                selectionIndex: 0);
            Assert.That(retargeted, Is.True);

            // An octave up reads the source twice as fast. Across the ramp the voice is still
            // getting there; the block after it, it has arrived.
            var acrossTheRamp = AdvanceAcrossBlock(voice, GainRamp.DeclickFrames);
            var afterTheRamp = AdvanceAcrossBlock(voice, GainRamp.DeclickFrames);

            Assert.That(acrossTheRamp, Is.GreaterThan(GainRamp.DeclickFrames * 1.05f), "the pitch never arrived");
            Assert.That(acrossTheRamp, Is.LessThan(GainRamp.DeclickFrames * 2f), "the pitch stepped instead of ramping");
            Assert.That(afterTheRamp, Is.EqualTo(GainRamp.DeclickFrames * 2f).Within(2f));
        }

        // Mixes one block and reports how many source frames the voice moved through, read off the
        // signal itself rather than from any position the voice publishes.
        private static float AdvanceAcrossBlock(Voice voice, int frameCount)
        {
            var busSamples = new float[frameCount * PlaybackFormat.ChannelCount];
            voice.MixBlock(busSamples, firstAbsoluteOutputFrame: 0, frameCount, out _);
            return busSamples[(frameCount - 1) * PlaybackFormat.ChannelCount] - busSamples[0];
        }

        // Sample value == source frame index, on both channels, so reading the output reads the
        // playhead. Interpolating a straight line reproduces it, so the readout stays exact under
        // resampling.
        private static float[] SourceFramePositionSignal()
        {
            var interleavedSamples = new float[SourceFrameCount * PlaybackFormat.ChannelCount];
            for (var frame = 0; frame < SourceFrameCount; frame++)
            {
                interleavedSamples[frame * PlaybackFormat.ChannelCount] = frame;
                interleavedSamples[frame * PlaybackFormat.ChannelCount + 1] = frame;
            }
            return interleavedSamples;
        }
    }
}
