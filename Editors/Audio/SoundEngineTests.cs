using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Cache;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Output;
using Moq;
using NAudio.Wave;
using Shared.Core.PackFiles;
using Shared.GameFormats.Audio.Formats.Pcm;

namespace Test.Audio
{
    public class SoundEngineTests
    {
        [Test]
        public void ScheduledVoices_StartOnExactSampleFrame_AndOverlap()
        {
            var mixer = new AudioMixer(maximumVoices: 4);
            var first = ConstantPcm(1f, 4);
            var second = ConstantPcm(1f, 4);
            mixer.SubmitCommand(MixerCommand.CreateTimeline(1, 8, false));
            mixer.SubmitCommand(MixerCommand.Schedule(1, 1, first, 0, false));
            mixer.SubmitCommand(MixerCommand.Schedule(1, 2, second, 2, false));
            mixer.SubmitCommand(MixerCommand.Start(1, 0));

            var output = new float[12];
            mixer.Read(output, 0, output.Length);

            Assert.That(output[0], Is.EqualTo(0.8f).Within(0.0001));
            Assert.That(output[2], Is.EqualTo(0.8f).Within(0.0001));
            Assert.That(output[4], Is.EqualTo(1f).Within(0.0001), "overlap is mixed and safely clipped");
            Assert.That(output[8], Is.EqualTo(0.8f).Within(0.0001));
        }

        [Test]
        public void Pause_DoesNotAdvanceTimelineOrVoices()
        {
            var mixer = new AudioMixer();
            var pcmSamples = ConstantPcm(0.5f, 20);
            mixer.SubmitCommand(MixerCommand.CreateTimeline(3, 20, false));
            mixer.SubmitCommand(MixerCommand.Schedule(3, 1, pcmSamples, 0, false));
            mixer.SubmitCommand(MixerCommand.Start(3, 0));
            mixer.Read(new float[8], 0, 8);
            Assert.That(mixer.PlaybackPositionFrames, Is.EqualTo(4));

            mixer.SubmitCommand(MixerCommand.Pause());
            var silence = new float[8];
            mixer.Read(silence, 0, silence.Length);

            Assert.That(mixer.PlaybackPositionFrames, Is.EqualTo(4));
            Assert.That(silence, Is.All.EqualTo(0f));
            Assert.That(mixer.RenderedOutputFrameCount, Is.EqualTo(8), "the absolute output clock advances while the timeline is paused");
        }

        [Test]
        public void ImmediateSilence_MutesAndFreezesBeforePauseCommandIsApplied()
        {
            var mixer = new AudioMixer();
            mixer.SubmitCommand(MixerCommand.Play(4, 1, ConstantPcm(1f, 20), 0, false));
            mixer.Read(new float[2], 0, 2);
            var positionBeforeMute = mixer.PlaybackPositionFrames;

            mixer.SilenceImmediately();
            var output = new float[8];
            mixer.Read(output, 0, output.Length);

            Assert.That(output, Is.All.EqualTo(0f));
            Assert.That(mixer.PlaybackPositionFrames, Is.EqualTo(positionBeforeMute));
        }

        [Test]
        public void ImmediatePlayback_PauseSeekResume_ContinuesFromAudibleFrame()
        {
            var mixer = new AudioMixer();
            var samples = Enumerable.Range(0, 20)
                .SelectMany(frame => new[] { frame / 20f, frame / 20f })
                .ToArray();
            mixer.SubmitCommand(MixerCommand.Play(5, 1, ToPcmData(samples), 0, false));
            mixer.Read(new float[20], 0, 20);

            mixer.SubmitCommand(MixerCommand.Pause());
            mixer.SubmitCommand(MixerCommand.Seek(5, 4));
            mixer.ProcessPendingCommandsWithoutRendering();
            mixer.SubmitCommand(MixerCommand.Resume());

            var resumed = new float[2];
            mixer.Read(resumed, 0, resumed.Length);

            Assert.That(mixer.PlaybackPositionFrames, Is.EqualTo(5));
            Assert.That(resumed[0], Is.EqualTo(0.2f * 0.8f).Within(0.0001f));
            Assert.That(resumed[1], Is.EqualTo(0.2f * 0.8f).Within(0.0001f));
        }

        [Test]
        public void Seek_ReconstructsAnInProgressScheduledVoice()
        {
            var mixer = new AudioMixer();
            var samples = Enumerable.Range(0, 10).SelectMany(x => new[] { x / 10f, x / 10f }).ToArray();
            mixer.SubmitCommand(MixerCommand.CreateTimeline(5, 20, false));
            mixer.SubmitCommand(MixerCommand.Schedule(5, 1, ToPcmData(samples), 3, false));
            mixer.SubmitCommand(MixerCommand.Start(5, 7));

            var output = new float[2];
            mixer.Read(output, 0, output.Length);

            Assert.That(output[0], Is.EqualTo(0.4f * 0.8f).Within(0.0001));
        }

        [Test]
        public void Loop_ReplaysSchedulesEveryTimelineCycle()
        {
            var mixer = new AudioMixer();
            var pcmSamples = ConstantPcm(1f, 1);
            mixer.SubmitCommand(MixerCommand.CreateTimeline(7, 3, true));
            mixer.SubmitCommand(MixerCommand.Schedule(7, 1, pcmSamples, 1, false));
            mixer.SubmitCommand(MixerCommand.Start(7, 0));

            var output = new float[14];
            mixer.Read(output, 0, output.Length);

            Assert.That(output[2], Is.EqualTo(0.8f).Within(0.0001));
            Assert.That(output[8], Is.EqualTo(0.8f).Within(0.0001));
        }

        [Test]
        public void OutputTimelineLedger_MapsDeviceFramesAcrossLoopBoundary()
        {
            var mixer = new AudioMixer();
            mixer.SubmitCommand(MixerCommand.CreateTimeline(8, 3, true));
            mixer.SubmitCommand(MixerCommand.Start(8, 0));
            mixer.Read(new float[10], 0, 10);

            Assert.That(mixer.TryGetTimelineFrame(0, 8, out var frame0), Is.True);
            Assert.That(frame0, Is.Zero);
            Assert.That(mixer.TryGetTimelineFrame(2, 8, out var frame2), Is.True);
            Assert.That(frame2, Is.EqualTo(2));
            Assert.That(mixer.TryGetTimelineFrame(3, 8, out var wrappedFrame), Is.True);
            Assert.That(wrappedFrame, Is.Zero);
            Assert.That(mixer.TryGetTimelineFrame(4, 8, out var nextFrame), Is.True);
            Assert.That(nextFrame, Is.EqualTo(1));
            Assert.That(mixer.TryGetTimelineFrame(3, 99, out _), Is.False, "a stale playback generation cannot drive animation");
        }

        [Test]
        public void OutputTimelineLedger_FreezesWhileImmediatelySilenced()
        {
            var mixer = new AudioMixer();
            mixer.SubmitCommand(MixerCommand.Play(10, 1, ConstantPcm(1f, 20), 0, false));
            mixer.Read(new float[4], 0, 4);
            mixer.SilenceImmediately();
            mixer.Read(new float[8], 0, 8);

            Assert.That(mixer.TryGetTimelineFrame(2, 10, out var firstSilentFrame), Is.True);
            Assert.That(firstSilentFrame, Is.EqualTo(2));
            Assert.That(mixer.TryGetTimelineFrame(5, 10, out var lastSilentFrame), Is.True);
            Assert.That(lastSilentFrame, Is.EqualTo(2));
        }

        [Test]
        public void TimelineCompletion_RecordsFirstDeviceFrameAfterFinalAudio()
        {
            var mixer = new AudioMixer();
            mixer.SubmitCommand(MixerCommand.CreateTimeline(12, 3, false));
            mixer.SubmitCommand(MixerCommand.Start(12, 0));
            mixer.Read(new float[6], 0, 6);

            Assert.That(mixer.CompletedPlaybackGeneration, Is.EqualTo(12));
            Assert.That(mixer.CompletedOutputFrame, Is.EqualTo(3));
        }

        [Test]
        public void VoiceLimit_DropsNewVoiceWithoutCuttingExistingVoice()
        {
            var mixer = new AudioMixer(maximumVoices: 1);
            mixer.SubmitCommand(MixerCommand.CreateTimeline(9, 10, false));
            mixer.SubmitCommand(MixerCommand.Schedule(9, 1, ConstantPcm(0.25f, 5), 0, false));
            mixer.SubmitCommand(MixerCommand.Schedule(9, 2, ConstantPcm(1f, 5), 0, false));
            mixer.SubmitCommand(MixerCommand.Start(9, 0));
            var output = new float[2];
            mixer.Read(output, 0, output.Length);
            Assert.That(output[0], Is.EqualTo(0.2f).Within(0.0001));
        }

        [Test]
        public void ImmediateVoices_OverlapWithoutReplacingEachOther()
        {
            var mixer = new AudioMixer(maximumVoices: 4);
            mixer.SubmitCommand(MixerCommand.Play(11, 1, ConstantPcm(0.25f, 4), 0, false));
            mixer.SubmitCommand(MixerCommand.Play(11, 2, ConstantPcm(0.25f, 4), 0, false));
            var output = new float[2];
            mixer.Read(output, 0, output.Length);
            Assert.That(output[0], Is.EqualTo(0.4f / MathF.Sqrt(2)).Within(0.0001));
        }

        [Test]
        public void CancellingOneVoice_LeavesTheOtherVoicesPlaying()
        {
            var mixer = new AudioMixer(maximumVoices: 4);
            mixer.SubmitCommand(MixerCommand.Play(20, 1, ConstantPcm(0.25f, 20), 0, false));
            mixer.SubmitCommand(MixerCommand.Play(20, 2, ConstantPcm(0.25f, 20), 0, false));
            mixer.Read(new float[2], 0, 2);

            mixer.SubmitCommand(MixerCommand.Cancel(20, 1));
            var output = new float[2];
            mixer.Read(output, 0, output.Length);

            Assert.That(output[0], Is.EqualTo(0.25f * 0.8f).Within(0.0001), "the surviving voice keeps playing");
        }

        [Test]
        public void Cache_ConvertsWaveToThePlaybackFormat()
        {
            var audio = CreateCache(1_000_000).GetWave(
                WaveBytes([0f, 0.25f, 0.5f, 0.75f], sampleRate: 24_000)).Audio;

            Assert.That(audio.SampleRate, Is.EqualTo(PlaybackFormat.SampleRate));
            Assert.That(audio.Channels, Is.EqualTo(PlaybackFormat.ChannelCount));
            Assert.That(audio.BitsPerSample, Is.EqualTo(PlaybackFormat.BitsPerSample));
        }

        [Test]
        public void Rendering_DoesNotAllocateAfterCommandsAreApplied()
        {
            var mixer = new AudioMixer();
            mixer.SubmitCommand(MixerCommand.Play(14, 1, ConstantPcm(0.25f, 10_000), 0, true));
            var output = new float[512];
            for (var iteration = 0; iteration < 100; iteration++)
                mixer.Read(output, 0, output.Length);

            var allocatedBytesBeforeRendering = GC.GetAllocatedBytesForCurrentThread();
            for (var iteration = 0; iteration < 100; iteration++)
                mixer.Read(output, 0, output.Length);
            var allocatedBytesAfterRendering = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(allocatedBytesAfterRendering, Is.EqualTo(allocatedBytesBeforeRendering));
        }

        [Test]
        public void CompletedVoice_IsReportedToTheControlSide()
        {
            var mixer = new AudioMixer();
            var voiceHandle = new VoiceHandle(41, 13);
            mixer.SubmitCommand(MixerCommand.Play(13, 41, ConstantPcm(1f, 1), 0, false));

            mixer.Read(new float[2], 0, 2);
            Assert.That(mixer.TryDequeueCompletedVoice(out _), Is.False);

            mixer.Read(new float[2], 0, 2);
            Assert.That(mixer.TryDequeueCompletedVoice(out var completedVoice), Is.True);
            Assert.That(completedVoice.VoiceHandle, Is.EqualTo(voiceHandle));
        }

        [Test]
        public async Task Cache_DeduplicatesConcurrentContent_AndSharesOneEntry()
        {
            var cache = CreateCache(1_000_000);
            var wave = WaveBytes(Enumerable.Repeat(0.25f, 100).ToArray());
            var tasks = Enumerable.Range(0, 16)
                .Select(_ => Task.Run(() => cache.GetWave(wave)))
                .ToArray();

            var audioEntries = await Task.WhenAll(tasks);
            Assert.That(cache.CachedAudioCount, Is.EqualTo(1));
            Assert.That(audioEntries.Select(entry => entry.ContentHash).Distinct().Count(), Is.EqualTo(1));
            Assert.That(audioEntries.Distinct().Count(), Is.EqualTo(1));
        }

        [Test]
        public void Cache_ResamplesWaveToMixerFormatAndNormalisesStereo()
        {
            var cache = CreateCache(1_000_000);
            var wave = WaveBytes([0f, 0.25f, 0.5f, 0.75f], sampleRate: 24_000);

            var audioEntry = cache.GetWave(wave);

            Assert.That(ToFloatSamples(audioEntry).Length, Is.EqualTo(16));
            for (var index = 0; index < ToFloatSamples(audioEntry).Length; index += 2)
                Assert.That(ToFloatSamples(audioEntry)[index], Is.EqualTo(ToFloatSamples(audioEntry)[index + 1]));
        }

        [Test]
        public void Cache_EvictsLeastRecentlyUsedEntry()
        {
            var cache = CreateCache(1_700);
            var firstWave = WaveBytes(Enumerable.Repeat(0.1f, 100).ToArray());
            var secondWave = WaveBytes(Enumerable.Repeat(0.2f, 100).ToArray());
            var thirdWave = WaveBytes(Enumerable.Repeat(0.3f, 100).ToArray());

            var first = cache.GetWave(firstWave);
            var second = cache.GetWave(secondWave);

            var recentlyUsedFirst = cache.GetWave(firstWave);
            var third = cache.GetWave(thirdWave);

            Assert.That(cache.CachedBytes, Is.LessThanOrEqualTo(cache.CapacityBytes));
            Assert.That(cache.CachedAudioCount, Is.EqualTo(2));

            var reacquiredFirst = cache.GetWave(firstWave);
            Assert.That(cache.CachedAudioCount, Is.EqualTo(2), "the recently used first entry remained cached");
        }

        [Test]
        public void SoundEngine_ReusesInjectedOutputDeviceAcrossImmediatePlayback()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 100).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            engine.Play(audio);
            engine.Play(audio);

            Assert.That(outputDevice.CreationCount, Is.EqualTo(1));
            Assert.That(outputDevice.EnsurePlayingCount, Is.EqualTo(2));
        }

        [Test]
        public void SoundEngine_ConcurrentVoicesShareTheCurrentPlaybackGeneration()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 100).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var firstVoice = engine.Play(audio);
            var secondVoice = engine.Play(audio);

            Assert.That(secondVoice.PlaybackGeneration, Is.EqualTo(firstVoice.PlaybackGeneration));
            Assert.That(secondVoice.VoiceIdentifier, Is.Not.EqualTo(firstVoice.VoiceIdentifier));
        }

        [Test]
        public async Task SoundEngine_ReportsCompletionOfTheVoiceThatFinished()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes([0.25f, 0.25f]));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var completedVoiceSource = new TaskCompletionSource<VoiceHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.VoiceCompleted += completedVoiceSource.SetResult;

            var voice = engine.Play(audio);
            outputDevice.Render(3);
            outputDevice.DevicePositionFrames = 3;

            var completedVoice = await completedVoiceSource.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.That(completedVoice, Is.EqualTo(voice));
        }

        // A device left running with nothing to render repeats the buffer it last rendered
        // whenever a callback is late, which is heard as a burst of buzzing over the audio
        // that was just cancelled.
        [Test]
        public void SoundEngine_CancellingTheLastAudibleVoice_StopsTheOutputDevice()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var voice = engine.Play(audio);
            outputDevice.Render(64);
            engine.Cancel(voice);

            Assert.That(outputDevice.FlushCount, Is.EqualTo(1));
            Assert.That(engine.PlaybackState, Is.EqualTo(SoundPlaybackState.Stopped));
        }

        [Test]
        public void SoundEngine_CancellingOneOfSeveralVoices_KeepsTheOutputDeviceRunning()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var cancelledVoice = engine.Play(audio);
            engine.Play(audio);
            outputDevice.Render(64);
            engine.Cancel(cancelledVoice);

            Assert.That(outputDevice.FlushCount, Is.EqualTo(0));
            Assert.That(engine.PlaybackState, Is.EqualTo(SoundPlaybackState.Playing));
        }

        [Test]
        public void SoundEngine_CancellingAVoiceWhileATimelineExists_LeavesTheTimelineIntact()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var timelineGeneration = engine.CreateTimeline(TimeSpan.FromSeconds(1), shouldLoop: false);
            engine.Start(timelineGeneration);
            var voice = engine.Play(audio);
            outputDevice.Render(64);
            engine.Cancel(voice);

            Assert.That(outputDevice.FlushCount, Is.EqualTo(0));
            Assert.That(engine.PlaybackGeneration, Is.EqualTo(timelineGeneration));
        }

        [Test]
        public void SoundEngine_PositionUsesInjectedDevicePosition()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 100).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var voice = engine.Play(audio);
            outputDevice.Render(10);
            outputDevice.DevicePositionFrames = 4;

            Assert.That(engine.Position, Is.EqualTo(TimeSpan.FromSeconds(4d / PlaybackFormat.SampleRate)));
            Assert.That(engine.GetPosition(voice), Is.EqualTo(TimeSpan.FromSeconds(4d / PlaybackFormat.SampleRate)));
        }

        // The waveform playhead paints from GetPosition, so a resume that reported a position
        // behind the one shown at the pause would visibly rewind the filled part of the graph.
        [Test]
        public void SoundEngine_ResumeAfterPause_DoesNotRewindTheReportedPosition()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var voice = engine.Play(audio);
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 40;
            var positionBeforePause = engine.GetPosition(voice);

            engine.Pause();
            // Output frames the physical device still drains while it is being stopped: the
            // absolute output clock advances over these, the timeline and the voices do not.
            outputDevice.Render(16);
            var positionWhilePaused = engine.GetPosition(voice);

            engine.Resume();
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

            var voice = engine.Play(audio);
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 40;

            engine.Pause();
            engine.Resume();
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

        // The output player is deliberately preserved across a pause, so EnsureOutputDeviceCreated
        // does not re-anchor the epoch on resume. If the device clock kept running while the output
        // was stopped, the epoch's device delta spans the whole pause and the playhead leaps forward
        // by however long the pause lasted.
        [Test]
        public void SoundEngine_DeviceClockRunningThroughPause_DoesNotJumpForwardOnResume()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var voice = engine.Play(audio);
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 40;
            var positionBeforePause = engine.GetPosition(voice);

            engine.Pause();
            outputDevice.DevicePositionFrames = 200;
            engine.Resume();
            outputDevice.Render(64);
            var positionAfterResume = engine.GetPosition(voice);

            Assert.That(positionBeforePause, Is.EqualTo(TimeSpan.FromSeconds(40d / PlaybackFormat.SampleRate)));
            Assert.That(positionAfterResume, Is.EqualTo(positionBeforePause), "a pause does not advance the audible position, however long it lasts");
        }

        // Stop anchors the epoch while the output is being stopped, and Play restarts a device
        // that already exists without re-anchoring it. Selecting a second file in the explorer
        // cancels the first voice, which stops the device, so the epoch's device delta spans the
        // gap between the two files.
        [Test]
        public void SoundEngine_PlayingAfterTheDeviceWasStopped_StartsTheNewVoiceAtZero()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var firstVoice = engine.Play(audio);
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 40;
            engine.Cancel(firstVoice);

            outputDevice.DevicePositionFrames = 200;
            var secondVoice = engine.Play(audio);
            outputDevice.Render(64);

            Assert.That(engine.GetPosition(secondVoice), Is.EqualTo(TimeSpan.Zero), "a newly started file begins at zero however long the device sat stopped");
        }

        // The same hazard on the timeline path SuperView drives: Start restarts an existing
        // device without re-anchoring the epoch that Stop left behind.
        [Test]
        public void SoundEngine_StartingATimelineAfterTheDeviceWasStopped_StartsAtZero()
        {
            var cache = CreateCache(1_000_000);
            var audio = cache.GetWave(WaveBytes(Enumerable.Repeat(0.25f, 4_000).ToArray()));
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);

            var firstGeneration = engine.CreateTimeline(TimeSpan.FromSeconds(1), shouldLoop: false);
            engine.Schedule(audio, TimeSpan.Zero, firstGeneration);
            engine.Start(firstGeneration);
            outputDevice.Render(64);
            outputDevice.DevicePositionFrames = 40;
            engine.Stop();

            outputDevice.DevicePositionFrames = 80;
            var secondGeneration = engine.CreateTimeline(TimeSpan.FromSeconds(1), shouldLoop: false);
            engine.Schedule(audio, TimeSpan.Zero, secondGeneration);
            engine.Start(secondGeneration);
            outputDevice.Render(64);

            Assert.That(engine.Position, Is.EqualTo(TimeSpan.Zero), "a newly started timeline begins at zero however long the device sat stopped");
        }

        private static SoundEngineCache CreateCache(long capacityBytes)
            => new(Mock.Of<IPackFileService>(), capacityBytes);

        private static byte[] ConstantPcm(float value, int frames)
            => ToPcmData(Enumerable.Repeat(value, frames * 2).ToArray());

        private static float[] ToFloatSamples(SoundEngineCacheEntry audioEntry)
            => audioEntry.Audio.ToInterleavedSamples();

        private static byte[] ToPcmData(float[] samples)
            => PcmAudio.CreateFromFloatSamples(samples, PlaybackFormat.ChannelCount, PlaybackFormat.SampleRate).Data;

        private static byte[] WaveBytes(float[] monoSamples, int sampleRate = 48_000)
        {
            using var stream = new MemoryStream();
            using (var writer = new WaveFileWriter(stream, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1)))
                writer.WriteSamples(monoSamples, 0, monoSamples.Length);
            return stream.ToArray();
        }

        private sealed class FakeAudioOutputDevice : IAudioOutputDevice
        {
            private ISampleProvider? _audioSource;

            public int CreationCount { get; private set; }
            public int EnsurePlayingCount { get; private set; }
            public int FlushCount { get; private set; }
            public long DevicePositionFrames { get; set; }
            public bool IsPlaying { get; private set; }

            public bool EnsureCreated(ISampleProvider audioSource)
            {
                if (_audioSource != null)
                    return false;

                _audioSource = audioSource;
                CreationCount++;
                return true;
            }

            public void EnsurePlaying()
            {
                EnsurePlayingCount++;
                IsPlaying = true;
            }

            public void FlushAndExecute(Action executeWhileStopped)
            {
                FlushCount++;
                IsPlaying = false;
                executeWhileStopped();
            }

            public void ExecuteIfNotPlaying(Action action)
            {
                if (IsPlaying)
                    return;

                action();
            }

            public AudioDevicePosition? CaptureOutputPosition()
                => _audioSource == null
                    ? null
                    : new AudioDevicePosition(
                        DevicePositionFrames * PlaybackFormat.ChannelCount * sizeof(float),
                        PlaybackFormat.WaveFormat.AverageBytesPerSecond);

            public void Render(int frameCount)
            {
                if (_audioSource == null)
                    throw new InvalidOperationException("The output device has not been created.");
                var buffer = new float[frameCount * PlaybackFormat.ChannelCount];
                _audioSource.Read(buffer, 0, buffer.Length);
            }

            public void Dispose()
            {
                IsPlaying = false;
                _audioSource = null;
            }
        }
    }
}
