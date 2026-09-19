using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Commands;
using Editors.Audio.Shared.Wwise.Engine.Rendering;

namespace Test.Audio
{
    // Sample discontinuities: the steps in the output that are heard as clicks.
    //
    // Each measurement compares the whole render against a clean stretch of the same render, so a
    // step is judged against the waveform's own slew rather than against a literal level. A
    // de-click ramp of a few milliseconds passes; a cut does not.
    //
    // The tests marked Explicit describe behaviour the engine does not have yet, and name the
    // phase that gives it to them. They are the target for that phase, not a report of today.
    public class SoundEngineContinuityTests
    {
        private static readonly TransportId Transport = new(1);
        private const int LeadInFrames = 256;
        private const int SoundingFrames = 512;

        // A ramp spreads a level change over its length, so it registers as a small step rather
        // than none at all. This is the room the measurement leaves for one.
        private const float PermittedStepMultiplier = 1.5f;

        [Test]
        public void TheSameAudio_RendersIdenticallyWhateverTheBufferSizes()
        {
            var reference = RenderScheduledTone(chunkFrames: 2_048);

            foreach (var chunkFrames in new[] { 1, 3, 64, 100, 333 })
                Assert.That(
                    RenderScheduledTone(chunkFrames),
                    Is.EqualTo(reference).Within(0.000001f),
                    $"the output changed when it was rendered {chunkFrames} frames at a time");
        }

        [Test]
        public void ALoopingVoice_WrapsWithoutADiscontinuity()
        {
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.PlayMedia(new PlayingId(1, Transport), default, MixerProbe.SeamlessTonePcm(512, 0.5f), 0, shouldLoop: true));

            var output = MixerProbe.Render(renderer, 2_048, 128);

            // The source holds a whole number of cycles, so every step in it is the waveform's
            // own. The first wrap is at frame 512.
            var stepBeforeTheFirstWrap = MixerProbe.MaximumStep(output, 0, 500);
            Assert.That(MixerProbe.MaximumStep(output), Is.LessThanOrEqualTo(stepBeforeTheFirstWrap * 1.25f));
        }

        [Test]
        public void AVoiceStartingOnItsOwnFirstSample_AddsNoStepOfItsOwn()
        {
            var output = RenderSilenceThenPlay(initialPcmFrame: 0);

            Assert.That(
                MixerProbe.MaximumStep(output),
                Is.LessThanOrEqualTo(StepWithinTheSettledTone(output) * PermittedStepMultiplier));
        }

        [Test]
        public void AVoiceStartingMidWaveform_DoesNotStepTheOutput()
        {
            var worstStepMultiplier = 0f;

            // Swept rather than sampled once, because a single offset can land on a zero crossing
            // and find nothing. The probe tone's period is not a whole number of frames, so no
            // offset repeats another's phase.
            for (var initialPcmFrame = 1; initialPcmFrame <= 127; initialPcmFrame++)
            {
                var output = RenderSilenceThenPlay(initialPcmFrame);
                worstStepMultiplier = MathF.Max(
                    worstStepMultiplier,
                    MixerProbe.MaximumStep(output) / StepWithinTheSettledTone(output));
            }

            Assert.That(worstStepMultiplier, Is.LessThanOrEqualTo(PermittedStepMultiplier));
        }

        [Test]
        public void PausingAndResuming_DoesNotStepTheOutput()
        {
            var renderer = new AudioRenderer();
            var playingId = new PlayingId(1, Transport);
            renderer.SubmitCommand(EngineCommand.PlayMedia(playingId, default, MixerProbe.ProbeTonePcm(8_192, 0.5f), 0, false));
            var beforePause = MixerProbe.Render(renderer, SoundingFrames, 128);
            renderer.SubmitCommand(EngineCommand.PausePlayingId(playingId));
            var whilePaused = MixerProbe.Render(renderer, LeadInFrames, 128);
            renderer.SubmitCommand(EngineCommand.ResumePlayingId(playingId, 0));
            var afterResume = MixerProbe.Render(renderer, SoundingFrames, 128);
            float[] output = [.. beforePause, .. whilePaused, .. afterResume];

            var stepWhilePlaying = MixerProbe.MaximumStep(output, 8, SoundingFrames - 16);
            Assert.That(MixerProbe.MaximumStep(output), Is.LessThanOrEqualTo(stepWhilePlaying * PermittedStepMultiplier));
        }

        [Test]
        public void StartingASilentVoice_ChangesNothingThatIsAlreadyPlaying()
        {
            // A voice carrying pure silence adds nothing to the mix, so any difference between
            // these two renders is the renderer reacting to the voice count rather than to audio.
            var withoutTheSilentVoice = RenderToneWithOptionalSilentVoice(shouldJoinSilentVoice: false);
            var withTheSilentVoice = RenderToneWithOptionalSilentVoice(shouldJoinSilentVoice: true);

            Assert.That(withTheSilentVoice, Is.EqualTo(withoutTheSilentVoice).Within(0.000001f));
        }

        [Test]
        public void OverlappingFullScaleCues_DoNotReachTheRail()
        {
            var renderer = new AudioRenderer(maximumVoices: 4);
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport, 2_048, false));
            for (var voiceIdentifier = 1; voiceIdentifier <= 3; voiceIdentifier++)
                renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(voiceIdentifier, Transport), default, MixerProbe.SingleSoundEvent(MixerProbe.ProbeTonePcm(1_024, 1f)), 0));
            renderer.SubmitCommand(EngineCommand.Start(Transport, 0));

            var output = MixerProbe.Render(renderer, 1_024, 128);

            Assert.That(MixerProbe.ClippedSampleCount(output), Is.Zero);
            Assert.That(MixerProbe.PeakAmplitude(output), Is.LessThan(1f));
        }

        private static float[] RenderScheduledTone(int chunkFrames)
        {
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport, 2_048, false));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(1, Transport), default, MixerProbe.SingleSoundEvent(MixerProbe.SeamlessTonePcm(1_024, 0.5f)), 97));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(new PlayingId(2, Transport), default, MixerProbe.SingleSoundEvent(MixerProbe.SeamlessTonePcm(512, 0.25f)), 613));
            renderer.SubmitCommand(EngineCommand.Start(Transport, 0));
            return MixerProbe.Render(renderer, 2_048, chunkFrames);
        }

        private static float[] RenderSilenceThenPlay(int initialPcmFrame)
        {
            var renderer = new AudioRenderer();
            var silence = MixerProbe.Render(renderer, LeadInFrames, 128);
            renderer.SubmitCommand(EngineCommand.PlayMedia(new PlayingId(1, Transport), default, MixerProbe.ProbeTonePcm(4_096, 0.5f), initialPcmFrame, false));
            var sounding = MixerProbe.Render(renderer, SoundingFrames, 128);
            return [.. silence, .. sounding];
        }

        private static float[] RenderToneWithOptionalSilentVoice(bool shouldJoinSilentVoice)
        {
            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.PlayMedia(new PlayingId(1, Transport), default, MixerProbe.SeamlessTonePcm(4_096, 0.5f), 0, false));
            var beforeTheJoin = MixerProbe.Render(renderer, SoundingFrames, 128);
            if (shouldJoinSilentVoice)
                renderer.SubmitCommand(EngineCommand.PlayMedia(new PlayingId(2, Transport), default, MixerProbe.SilentPcm(4_096), 0, false));
            var afterTheJoin = MixerProbe.Render(renderer, SoundingFrames, 128);
            return [.. beforeTheJoin, .. afterTheJoin];
        }

        // Measured well past the start, so a ramp at the start is not mistaken for the waveform's
        // natural slew and used to excuse itself.
        private static float StepWithinTheSettledTone(float[] output)
            => MixerProbe.MaximumStep(output, LeadInFrames + 300, 200);
    }
}
