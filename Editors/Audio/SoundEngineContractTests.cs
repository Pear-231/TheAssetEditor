using System.Runtime.CompilerServices;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Commands;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Rendering;
using Moq;
using NAudio.Wave;
using Shared.Core.PackFiles;
using Shared.GameFormats.Audio.Containers.Wav;
using Shared.GameFormats.Wwise.Didx;

namespace Test.Audio
{
    public class SoundEngineContractTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ImmediatePlayback_LeavesAStoppedPreparedTimelineIntact(bool postEvent)
        {
            var renderer = new AudioRenderer();
            var preparedTransport = new TransportId(1);
            var immediateTransport = new TransportId(2);
            renderer.SubmitCommand(EngineCommand.CreateTimeline(preparedTransport, 100, false));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(
                new PlayingId(1, preparedTransport),
                default,
                MixerProbe.SingleSoundEvent(MixerProbe.ConstantPcm(0.5f, 20)),
                0));
            var playingId = new PlayingId(2, immediateTransport);
            if (postEvent)
                renderer.SubmitCommand(EngineCommand.PostEvent(
                    playingId,
                    default,
                    MixerProbe.SingleSoundEvent(MixerProbe.ConstantPcm(0.25f, 20))));
            else
                renderer.SubmitCommand(EngineCommand.PlayMedia(
                    playingId,
                    default,
                    MixerProbe.ConstantPcm(0.25f, 20),
                    0,
                    false));

            var output = MixerProbe.Render(renderer, 4, 1);

            Assert.That(output[0], Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(renderer.TryGetTimelineFrame(0, preparedTransport, out var preparedFrame), Is.True);
            Assert.That(preparedFrame, Is.Zero, "the prepared timeline remains stopped");
            Assert.That(renderer.TryGetTimelineFrame(0, immediateTransport, out _), Is.False);
        }

        [Test]
        public void RefusedPost_ReachesATerminalOutcome()
        {
            var renderer = new AudioRenderer(maximumVoices: 1);
            var firstPlayingId = new PlayingId(1, new TransportId(1));
            var refusedPlayingId = new PlayingId(2, new TransportId(1));
            var firstCompletion = new PostCompletionState(firstPlayingId);
            var refusedCompletion = new PostCompletionState(refusedPlayingId);
            renderer.SubmitCommand(EngineCommand.PlayMedia(
                firstPlayingId, default, MixerProbe.ConstantPcm(0.25f, 100), 0, false, firstCompletion));
            renderer.SubmitCommand(EngineCommand.PlayMedia(
                refusedPlayingId, default, MixerProbe.ConstantPcm(0.25f, 100), 0, false, refusedCompletion));

            MixerProbe.Render(renderer, 1, 1);

            Assert.That(firstCompletion.IsCompleted, Is.False);
            Assert.That(refusedCompletion.TryGetCompletion(out var outcome, out var outputFrame), Is.True);
            Assert.That(outcome, Is.EqualTo(PostOutcome.Refused));
            Assert.That(outputFrame, Is.Zero);
        }

        [Test]
        public void SilentPost_ReachesATerminalOutcome()
        {
            var builder = new ResolvedEventBuilder();
            builder.AddRoot(builder.AddSound(1, 1, null, VoiceLimit.None));
            var renderer = new AudioRenderer();
            var playingId = new PlayingId(1, new TransportId(1));
            var completion = new PostCompletionState(playingId);
            renderer.SubmitCommand(EngineCommand.PostEvent(playingId, default, builder.Build("silent"), completion));

            MixerProbe.Render(renderer, 1, 1);

            Assert.That(completion.TryGetCompletion(out var outcome, out _), Is.True);
            Assert.That(outcome, Is.EqualTo(PostOutcome.Silent));
            Assert.That(renderer.Telemetry.SilentCueCount, Is.EqualTo(1));
        }

        [Test]
        public void ScheduledPostOutsideTheTimeline_IsRefused()
        {
            var renderer = new AudioRenderer();
            var transportId = new TransportId(1);
            var playingId = new PlayingId(1, transportId);
            var completion = new PostCompletionState(playingId);
            renderer.SubmitCommand(EngineCommand.CreateTimeline(transportId, 10, false));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(
                playingId,
                default,
                MixerProbe.SingleSoundEvent(MixerProbe.ConstantPcm(0.25f, 4)),
                10,
                completion));
            renderer.ProcessPendingCommandsWithoutRendering();

            Assert.That(completion.TryGetCompletion(out var outcome, out _), Is.True);
            Assert.That(outcome, Is.EqualTo(PostOutcome.Refused));
        }

        [Test]
        public void StartingPastAScheduledPost_CompletesItAtTheSeekPoint()
        {
            var renderer = new AudioRenderer();
            var transportId = new TransportId(1);
            var playingId = new PlayingId(1, transportId);
            var completion = new PostCompletionState(playingId);
            renderer.SubmitCommand(EngineCommand.CreateTimeline(transportId, 20, false));
            renderer.SubmitCommand(EngineCommand.ScheduleEvent(
                playingId,
                default,
                MixerProbe.SingleSoundEvent(MixerProbe.ConstantPcm(0.25f, 4)),
                0,
                completion));
            renderer.SubmitCommand(EngineCommand.Start(transportId, 10));

            MixerProbe.Render(renderer, 1, 1);

            Assert.That(completion.TryGetCompletion(out var outcome, out _), Is.True);
            Assert.That(outcome, Is.EqualTo(PostOutcome.Played));
        }

        [Test]
        public void SelectedSoundsBeyondTheVoiceBudget_AreCounted()
        {
            var builder = new ResolvedEventBuilder();
            var soundIndices = Enumerable.Range(1, 3)
                .Select(id => builder.AddSound((uint)id, (uint)id, MixerProbe.ConstantPcm(0.1f, 4), VoiceLimit.None))
                .ToList();
            builder.AddRoot(builder.AddContainer(ResolvedNodeKind.All, 10, null, soundIndices, null));
            var renderer = new AudioRenderer(maximumVoices: 2);
            var playingId = new PlayingId(1, new TransportId(1));
            var completion = new PostCompletionState(playingId);
            renderer.SubmitCommand(EngineCommand.PostEvent(playingId, default, builder.Build("layer"), completion));

            MixerProbe.Render(renderer, 6, 1);

            Assert.That(renderer.Telemetry.UnstartedVoiceCount, Is.EqualTo(1));
            Assert.That(completion.TryGetCompletion(out var outcome, out _), Is.True);
            Assert.That(outcome, Is.EqualTo(PostOutcome.Played));
        }

        [Test]
        public void CompletingAPost_DoesNotAllocateInTheRenderCallback()
        {
            var renderer = new AudioRenderer();
            var warmPlayingId = new PlayingId(1, new TransportId(1));
            renderer.SubmitCommand(EngineCommand.PlayMedia(
                warmPlayingId,
                default,
                MixerProbe.ConstantPcm(0.25f, 1),
                0,
                false,
                new PostCompletionState(warmPlayingId)));
            var output = new float[4];
            renderer.Render(output, 0, output.Length);

            var measuredPlayingId = new PlayingId(2, new TransportId(2));
            var completion = new PostCompletionState(measuredPlayingId);
            renderer.SubmitCommand(EngineCommand.PlayMedia(
                measuredPlayingId,
                default,
                MixerProbe.ConstantPcm(0.25f, 1),
                0,
                false,
                completion));
            var allocatedBytesBeforeRendering = GC.GetAllocatedBytesForCurrentThread();

            renderer.Render(output, 0, output.Length);

            Assert.That(GC.GetAllocatedBytesForCurrentThread(), Is.EqualTo(allocatedBytesBeforeRendering));
            Assert.That(completion.IsCompleted, Is.True);
        }

        [Test]
        public void SourceMedia_OwnsAValidatedImmutableCopy()
        {
            var suppliedSamples = new[] { 0.25f, -0.25f };
            var media = new SourceMedia("content", suppliedSamples, 1, 48_000);
            suppliedSamples[0] = 1f;

            Assert.That(media.Samples[0], Is.EqualTo(0.25f));
            Assert.Throws<ArgumentException>(() => new SourceMedia("empty", [], 1, 48_000));
            Assert.Throws<ArgumentException>(() => new SourceMedia("partial", [0f, 0f, 0f], 2, 48_000));
            Assert.Throws<ArgumentException>(() => new SourceMedia("non-finite", [float.NaN], 1, 48_000));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RepositoryHierarchy_ResolvesDuplicateSourceIdsByReferringBank(bool reverseLookupOrder)
        {
            byte[] firstWem = [1];
            byte[] secondWem = [2];
            var repository = new Mock<IAudioRepository>();
            repository.Setup(value => value.FindDidxWem(17)).Returns(
            [
                new DidxAudio { Id = 17, OwnerFilePath = "first.bnk", ByteArray = firstWem },
                new DidxAudio { Id = 17, OwnerFilePath = "second.bnk", ByteArray = secondWem }
            ]);
            var hierarchy = new AudioRepositoryHierarchy(
                repository.Object,
                wemBytes => new SourceMedia(
                    Convert.ToHexString(wemBytes),
                    [wemBytes[0] / 10f],
                    1,
                    48_000));

            var first = reverseLookupOrder
                ? hierarchy.FindMedia(17, "second.bnk")
                : hierarchy.FindMedia(17, "first.bnk");
            var second = reverseLookupOrder
                ? hierarchy.FindMedia(17, "first.bnk")
                : hierarchy.FindMedia(17, "second.bnk");

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(first!.ContentHash, Is.Not.EqualTo(second!.ContentHash));
            Assert.That(first.Samples[0], Is.EqualTo(reverseLookupOrder ? 0.2f : 0.1f).Within(0.0001f));
            Assert.That(second.Samples[0], Is.EqualTo(reverseLookupOrder ? 0.1f : 0.2f).Within(0.0001f));
        }

        [Test]
        public void RepositoryHierarchy_ReResolvesMediaAfterRepositoryContentChanges()
        {
            var repository = new Mock<IAudioRepository>();
            repository.SetupSequence(value => value.FindDidxWem(17))
                .Returns([new DidxAudio { Id = 17, OwnerFilePath = "audio.bnk", ByteArray = [1] }])
                .Returns([new DidxAudio { Id = 17, OwnerFilePath = "audio.bnk", ByteArray = [2] }]);
            var hierarchy = new AudioRepositoryHierarchy(
                repository.Object,
                wemBytes => new SourceMedia(Convert.ToHexString(wemBytes), [wemBytes[0] / 10f], 1, 48_000));

            var beforeReload = hierarchy.FindMedia(17, "audio.bnk");
            var afterReload = hierarchy.FindMedia(17, "audio.bnk");

            Assert.That(beforeReload!.Samples[0], Is.EqualTo(0.1f));
            Assert.That(afterReload!.Samples[0], Is.EqualTo(0.2f));
        }

        [Test]
        public void EvictedRepositoryMedia_IsCollectibleWhenNothingUsesIt()
        {
            var firstWave = WaveBytes(Enumerable.Repeat(0.1f, 100).ToArray());
            var secondWave = WaveBytes(Enumerable.Repeat(0.2f, 100).ToArray());
            var repository = new Mock<IAudioRepository>();
            repository.Setup(value => value.FindDidxWem(1)).Returns(
                [new DidxAudio { Id = 1, OwnerFilePath = "audio.bnk", ByteArray = firstWave }]);
            repository.Setup(value => value.FindDidxWem(2)).Returns(
                [new DidxAudio { Id = 2, OwnerFilePath = "audio.bnk", ByteArray = secondWave }]);
            var cache = CreateCache(500);
            var hierarchy = new AudioRepositoryHierarchy(repository.Object, wemBytes => cache.GetWave(wemBytes));

            var evictedMedia = LoadThenEvict(hierarchy);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(evictedMedia.IsAlive, Is.False);
        }

        [Test]
        public async Task RefusedEnginePost_RaisesPostCompletedExactlyOnce()
        {
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var media = CreateCache(1_000_000).GetWave(WaveBytes(Enumerable.Repeat(0.25f, 1_000).ToArray()));
            var completed = new List<PlayingId>();
            var refusedCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PlayingId refusedPlayingId = default;
            engine.PostCompleted += playingId =>
            {
                lock (completed)
                    completed.Add(playingId);
                if (playingId == refusedPlayingId)
                    refusedCompletion.TrySetResult();
            };

            for (var postOrdinal = 0; postOrdinal < 33; postOrdinal++)
                refusedPlayingId = engine.PlayMedia(media);
            outputDevice.Render(1);
            outputDevice.DevicePositionFrames = 1;

            await refusedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await Task.Delay(75);

            lock (completed)
                Assert.That(completed.Count(playingId => playingId == refusedPlayingId), Is.EqualTo(1));
        }

        [TestCase((ushort)1, (ushort)8, new byte[] { 192 })]
        [TestCase((ushort)1, (ushort)16, new byte[] { 0, 64 })]
        [TestCase((ushort)1, (ushort)24, new byte[] { 0, 0, 64 })]
        [TestCase((ushort)1, (ushort)32, new byte[] { 0, 0, 0, 64 })]
        [TestCase((ushort)3, (ushort)32, new byte[] { 0, 0, 0, 63 })]
        public void SupportedClassicWaveFormats_AreDecodedThroughMediaCache(ushort formatTag, ushort bitsPerSample, byte[] sampleData)
        {
            var media = CreateCache(1_000_000).GetWave(ClassicWaveBytes(formatTag, bitsPerSample, sampleData));

            Assert.That(media.Samples[0], Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void UnsupportedAndMalformedWaveFormats_AreRejected()
        {
            var unsupported = WaveBytes([0.25f]);
            unsupported[20] = 6;
            unsupported[21] = 0;

            var invalidAlignment = WaveBytes([0.25f]);
            invalidAlignment[32] = 1;
            invalidAlignment[33] = 0;

            var cache = CreateCache(1_000_000);
            Assert.Throws<InvalidDataException>(() => cache.GetWave(unsupported));
            Assert.Throws<InvalidDataException>(() => cache.GetWave(invalidAlignment));
            Assert.Throws<InvalidDataException>(() => cache.GetWave(ClassicWaveBytes(1, 16, [0])));
        }

        [Test]
        public void ExtensiblePcmWave_IsDecoded()
        {
            var wave = ExtensiblePcmWaveBytes();

            var media = CreateCache(1_000_000).GetWave(wave);

            Assert.That(media.Samples[0], Is.EqualTo(0.5f).Within(0.001f));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference LoadThenEvict(AudioRepositoryHierarchy hierarchy)
        {
            var first = hierarchy.FindMedia(1, "audio.bnk");
            var weakReference = new WeakReference(first!);
            hierarchy.FindMedia(2, "audio.bnk");
            return weakReference;
        }

        private static MediaCache CreateCache(long capacityBytes)
            => new(Mock.Of<IPackFileService>(), capacityBytes);

        private static byte[] WaveBytes(float[] monoSamples)
        {
            using var stream = new MemoryStream();
            using (var writer = new WaveFileWriter(stream, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1)))
                writer.WriteSamples(monoSamples, 0, monoSamples.Length);
            return stream.ToArray();
        }

        private static byte[] ClassicWaveBytes(ushort formatTag, ushort bitsPerSample, byte[] sampleData)
        {
            var blockAlign = checked((ushort)(bitsPerSample / 8));
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write("RIFF"u8);
            writer.Write(36 + sampleData.Length);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write(formatTag);
            writer.Write((ushort)1);
            writer.Write(48_000u);
            writer.Write(48_000u * blockAlign);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);
            writer.Write("data"u8);
            writer.Write(sampleData.Length);
            writer.Write(sampleData);
            return stream.ToArray();
        }

        private static byte[] ExtensiblePcmWaveBytes()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write("RIFF"u8);
            writer.Write(62);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(40);
            writer.Write(Shared.GameFormats.Audio.Containers.Wav.FmtChunk.ExtensibleFormatTag);
            writer.Write((ushort)1);
            writer.Write(48_000u);
            writer.Write(96_000u);
            writer.Write((ushort)2);
            writer.Write((ushort)16);
            writer.Write((ushort)22);
            writer.Write((ushort)16);
            writer.Write(4u);
            writer.Write(new Guid(1, 0, 0x0010, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71).ToByteArray());
            writer.Write("data"u8);
            writer.Write(2);
            writer.Write((short)16_384);
            return stream.ToArray();
        }
    }
}
