using Shared.GameFormats.Wwise;
using System.Numerics;
using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Rendering;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Editors.Audio.Shared.Wwise.Engine.Timing;
using Moq;
using Shared.Core.PackFiles;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Test.Audio
{
    public class Phase13CompletionTests
    {
        [Test]
        public void WaveSamplerLoop_IsCarriedOntoSourceMedia()
        {
            var cache = new MediaCache(Mock.Of<IPackFileService>());

            var media = cache.GetWave(WaveWithLoop());

            Assert.Multiple(() =>
            {
                Assert.That(media.LoopStartFrame, Is.EqualTo(1));
                Assert.That(media.LoopEndFrame, Is.EqualTo(3));
                Assert.That(media.HasLoopRegion, Is.True);
            });
        }

        [Test]
        public void LoopingVoice_WrapsAtAuthoredMediaLoop()
        {
            var media = new SourceMedia("loop", [0.1f, 0.2f, 0.3f, 0.4f], 1, PlaybackFormat.SampleRate, loopStartFrame: 1, loopEndFrame: 3);
            var voices = new VoicePool(2, new EngineTelemetry());
            var playingId = new PlayingId(1, default);
            voices.Activate(VoiceStart.ForMedia(playingId, new PostCompletionState(playingId), media, VoiceParameters.Neutral, 0, isLooping: true));

            var output = Mix(voices, 7);

            Assert.That(Left(output), Is.EqualTo(new[] { 0.1f, 0.2f, 0.3f, 0.2f, 0.3f, 0.2f, 0.3f }).Within(0.0001f));
        }

        [Test]
        public void SurroundDownmix_UsesEveryChannelInTheSpeakerMask()
        {
            const uint fivePointOneMask = 0x3F;
            var samples = Enumerable.Range(0, 16)
                .SelectMany(_ => new[] { 1f, 2f, 3f, 4f, 5f, 6f })
                .ToArray();
            var media = new SourceMedia("surround", samples, 6, PlaybackFormat.SampleRate, fivePointOneMask);
            var output = Play(media, 1);

            Assert.Multiple(() =>
            {
                Assert.That(output[0], Is.EqualTo(1f + 3f * 0.70710678f + 4f * 0.5f + 5f * 0.70710678f).Within(0.0001f));
                Assert.That(output[1], Is.EqualTo(2f + 3f * 0.70710678f + 4f * 0.5f + 6f * 0.70710678f).Within(0.0001f));
            });
        }

        [Test]
        public void BandLimitedResampler_PassesAudioBandAndRejectsDownsampleAliases()
        {
            var passband = ResampleTone(5_000d);
            var stopband = ResampleTone(30_000d);

            Assert.Multiple(() =>
            {
                Assert.That(RootMeanSquare(passband), Is.GreaterThan(0.65f));
                Assert.That(RootMeanSquare(stopband), Is.LessThan(0.08f));
                Assert.That(Resampler.GroupDelayFrames, Is.Zero);
            });
        }

        // The V112 hierarchy reaches BusGraph with no change to BusGraph, which is the test of
        // whether phase 15's split was in the right place: a missing reader was the whole defect.
        [Test]
        public void MasterMixerHierarchy_BuildsFromAttilaBusesToo()
        {
            var parent = AttilaBus(10, outputBusId: 0, -6f);
            var child = AttilaBus(20, outputBusId: 10, -6f);
            var telemetry = new EngineTelemetry();
            var graph = BusGraph.Create([child, parent], 8, telemetry);
            var master = new Bus(8);
            var timelineGain = new GainRamp();
            timelineGain.SetImmediately(1f);
            graph.Clear(1);
            master.Clear(1);
            graph.Route(20, isTimelineVoice: false).Samples[0] = 1f;

            graph.MixTo(master, timelineGain, 1, telemetry);

            Assert.That(master.Samples[0], Is.EqualTo(MathF.Pow(10f, -12f / 20f)).Within(0.0001f));
        }

        [Test]
        public void MasterMixerHierarchy_AppliesEachBusGainInDeterministicOrder()
        {
            var parent = Bus(10, outputBusId: 0, -6f);
            var child = Bus(20, outputBusId: 10, -6f);
            child.BusInitialFxParams.FxChunk.Add(new FxChunk_V136 { FxId = 123 });
            child.BusInitialParams.AuxParams.AuxBus0 = 456;
            var telemetry = new EngineTelemetry();
            var graph = BusGraph.Create([child, parent], 8, telemetry);
            var master = new Bus(8);
            var timelineGain = new GainRamp();
            timelineGain.SetImmediately(1f);
            graph.Clear(1);
            master.Clear(1);
            graph.Route(20, isTimelineVoice: false).Samples[0] = 1f;
            graph.Route(20, isTimelineVoice: false).Samples[1] = 1f;

            graph.MixTo(master, timelineGain, 1, telemetry);

            Assert.Multiple(() =>
            {
                Assert.That(master.Samples[0], Is.EqualTo(MathF.Pow(10f, -12f / 20f)).Within(0.0001f));
                Assert.That(telemetry.UnsupportedEffectCount, Is.EqualTo(1));
                Assert.That(telemetry.UnsupportedAuxiliarySendCount, Is.EqualTo(1));
                Assert.That(telemetry.HighestPeakBusId, Is.AnyOf(10u, 20u));
            });
        }

        [Test]
        public void PositionAndAttenuation_UpdateAnActiveVoiceThroughPublishedSnapshots()
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(2, telemetry);
            var parameters = new GameObjectParameterSource();
            parameters.Publish(new GameObjectParameters([], new Vector3(1f, 0f, 0f), [Vector3.Zero]));
            var attenuation = new AttenuationSettings(
                new WwiseCurve(AkCurveScaling.None, [new WwiseCurvePoint(0f, 0f, 0), new WwiseCurvePoint(1f, -6f, 0)]),
                WwiseCurve.Empty,
                WwiseCurve.Empty);
            var media = MixerProbe.ConstantPcm(1f, 1_000);
            var playingId = new PlayingId(1, default);
            voices.Activate(new VoiceStart(
                playingId,
                new PostCompletionState(playingId),
                media,
                VoiceParameters.Neutral,
                VoiceLimit.None,
                ReadOnlyMemory<VoiceLimit>.Empty,
                1,
                default,
                default,
                0,
                0,
                0,
                false,
                false,
                Attenuation: attenuation,
                GameObjectParameters: parameters,
                IsPositioned: true));

            var right = Mix(voices, 1);
            parameters.Publish(new GameObjectParameters([], new Vector3(-1f, 0f, 0f), [Vector3.Zero]));
            var left = Mix(voices, 1);

            Assert.Multiple(() =>
            {
                Assert.That(right[0], Is.EqualTo(0f).Within(0.001f));
                Assert.That(right[1], Is.GreaterThan(0.7f));
                Assert.That(left[0], Is.GreaterThan(0.7f));
                Assert.That(left[1], Is.EqualTo(0f).Within(0.001f));
            });
        }

        [Test]
        public void AttenuatedVoice_CrossesVirtualThresholdInBothDirections()
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(2, telemetry);
            var parameters = new GameObjectParameterSource();
            parameters.Publish(new GameObjectParameters([], new Vector3(10f, 0f, 0f), [Vector3.Zero]));
            var attenuation = new AttenuationSettings(
                new WwiseCurve(AkCurveScaling.None, [new WwiseCurvePoint(0f, 0f, 0), new WwiseCurvePoint(10f, -120f, 0)]),
                WwiseCurve.Empty,
                WwiseCurve.Empty);
            var playingId = new PlayingId(1, default);
            voices.Activate(new VoiceStart(
                playingId,
                new PostCompletionState(playingId),
                MixerProbe.ConstantPcm(1f, 1_000),
                VoiceParameters.Neutral,
                VoiceLimit.None,
                ReadOnlyMemory<VoiceLimit>.Empty,
                1,
                default,
                default,
                0,
                0,
                0,
                false,
                false,
                Attenuation: attenuation,
                GameObjectParameters: parameters,
                IsPositioned: true,
                BelowThresholdBehaviour: 2));

            Mix(voices, 8);
            parameters.Publish(new GameObjectParameters([], Vector3.Zero, [Vector3.Zero]));
            Mix(voices, GainRamp.DeclickFrames);

            Assert.Multiple(() =>
            {
                Assert.That(telemetry.CurrentVirtualVoiceCount, Is.Zero);
                Assert.That(telemetry.CurrentPhysicalVoiceCount, Is.EqualTo(1));
                Assert.That(telemetry.PeakVirtualVoiceCount, Is.EqualTo(1));
                Assert.That(telemetry.VoiceThresholdTransitionCount, Is.EqualTo(2));
            });
        }

        [Test]
        public void UnboundedContinuousSequence_SelectsBeyondWarmVoiceCapacity()
        {
            var hierarchy = new FakeHierarchy()
                .WithSound(1, MixerProbe.ConstantPcm(0.25f, 4))
                .WithSound(2, MixerProbe.ConstantPcm(0.5f, 4))
                .WithSequenceContainer(10, 1, 2)
                .AsContinuous(loopCount: 0)
                .WithEvent("loop", 10);
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("emitter");
            engine.PostEvent("loop", gameObject);

            var output = outputDevice.Render(24);

            Assert.That(Left(output), Is.EqualTo(new[]
            {
                0.25f, 0.25f, 0.25f, 0.25f, 0.5f, 0.5f, 0.5f, 0.5f,
                0.25f, 0.25f, 0.25f, 0.25f, 0.5f, 0.5f, 0.5f, 0.5f,
                0.25f, 0.25f, 0.25f, 0.25f, 0.5f, 0.5f, 0.5f, 0.5f
            }).Within(0.0001f));
        }

        [Test]
        public void TimelineEnd_RampsTheCutVoiceBeforeCompleting()
        {
            var hierarchy = new FakeHierarchy()
                .WithSound(1, MixerProbe.ConstantPcm(1f, 100))
                .WithEvent("end", 1);
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("emitter");
            var timeline = engine.CreateTimeline(TimeSpan.FromSeconds(8d / PlaybackFormat.SampleRate), shouldLoop: false);
            engine.ScheduleEvent("end", gameObject, TimeSpan.Zero, timeline);
            engine.Start(timeline);

            var output = Left(outputDevice.Render(8));

            Assert.Multiple(() =>
            {
                Assert.That(output, Is.Ordered.Descending);
                Assert.That(output[0], Is.LessThan(1f));
                Assert.That(output[^1], Is.EqualTo(0f).Within(0.0001f));
            });
        }

        // A cross-fade between two arbitrary points of the same tone sums two copies at different
        // phases, and over the fade the resultant phase slews from one to the other -- a momentary
        // pitch bend, measured at 76 frames per cycle against a steady 109 before this. Lengthening
        // the fade only shrank it. Landing where the waveform continues removes the phase
        // difference instead of spreading it.
        [Test]
        public void SeekingLandsWhereTheWaveformContinues()
        {
            const int SeekFrame = 3_000;
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var playingId = engine.PlayMedia(Tone(440f, 20_000));
            outputDevice.Render(1_000);

            engine.Seek(playingId, PlaybackTime.FromFrames(SeekFrame));
            var rendered = outputDevice.Render(GainRamp.SeekCrossFadeFrames * 2);

            // A steady 440 Hz sine at 48 kHz crosses zero every 109.09 frames. Every cycle across
            // the seek has to stay there: a phase slew shows up as cycles that are short or long.
            var crossings = new List<int>();
            for (var frame = 1; frame < rendered.Length / PlaybackFormat.ChannelCount; frame++)
            {
                if (Left(rendered)[frame - 1] <= 0f && Left(rendered)[frame] > 0f)
                    crossings.Add(frame);
            }

            Assert.That(crossings, Has.Count.GreaterThan(4), "the tone is sounding across the seek");
            for (var index = 1; index < crossings.Count; index++)
            {
                Assert.That(
                    crossings[index] - crossings[index - 1],
                    Is.InRange(108, 111),
                    "every cycle across the seek stays at the tone’s own period");
            }
        }

        private static SourceMedia Tone(float hertz, int frameCount)
        {
            var samples = new float[frameCount * PlaybackFormat.ChannelCount];
            for (var frame = 0; frame < frameCount; frame++)
            {
                var value = 0.5f * MathF.Sin(2f * MathF.PI * hertz * frame / PlaybackFormat.SampleRate);
                samples[frame * PlaybackFormat.ChannelCount] = value;
                samples[frame * PlaybackFormat.ChannelCount + 1] = value;
            }
            return MixerProbe.ToMedia(samples);
        }

        // A seek is two discontinuities, not one: the old position stops being played and a new one
        // starts. Ramping only the new side leaves the old one cut off mid-waveform, which is the
        // click phase 13 owed. These two tests drive a seek while the voice is actually sounding -
        // a paused voice has nothing to fade out - and read the crossfade off a ramp whose sample
        // value states which position produced it.
        [Test]
        public void SeekingAPlayingWaveform_FadesTheOldPositionOutAcrossTheNewOneFadingIn()
        {
            const int PlayedFrames = 64;
            const int SeekFrame = 2_000;
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            var playingId = engine.PlayMedia(PositionRamp(8_000));
            outputDevice.Render(PlayedFrames);
            outputDevice.DevicePositionFrames = PlayedFrames;

            engine.Seek(playingId, PlaybackTime.FromFrames(SeekFrame));
            var crossfade = outputDevice.Render(GainRamp.SeekCrossFadeFrames);
            var afterCrossfade = Left(outputDevice.Render(1));

            AssertCrossfade(crossfade, afterCrossfade[0], PlayedFrames, SeekFrame + GainRamp.SeekCrossFadeFrames);
        }

        [Test]
        public void SeekingAPlayingTimeline_FadesTheOldPositionOutAcrossTheNewOneFadingIn()
        {
            const int PlayedFrames = 64;
            const int SeekFrame = 2_000;
            var hierarchy = new FakeHierarchy()
                .WithSound(1, PositionRamp(8_000))
                .WithEvent("cue", 1);
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("emitter");
            var timeline = engine.CreateTimeline(PlaybackTime.FromFrames(8_000), shouldLoop: false);
            engine.ScheduleEvent("cue", gameObject, TimeSpan.Zero, timeline);
            engine.Start(timeline);
            outputDevice.Render(PlayedFrames);
            outputDevice.DevicePositionFrames = PlayedFrames;

            engine.Seek(timeline, PlaybackTime.FromFrames(SeekFrame));
            var crossfade = outputDevice.Render(GainRamp.SeekCrossFadeFrames);
            var afterCrossfade = Left(outputDevice.Render(1));

            AssertCrossfade(crossfade, afterCrossfade[0], PlayedFrames, SeekFrame + GainRamp.SeekCrossFadeFrames);
        }

        // The old position has to still be audible on the first frame after the seek, the new one
        // has to be the only thing left once the ramp is over, and nothing in between may step
        // harder than the ramp itself does.
        private static void AssertCrossfade(float[] crossfade, float afterCrossfade, int oldFrame, int newFrame)
        {
            var seekedDistance = MathF.Abs(PositionOf(newFrame) - PositionOf(oldFrame));
            var rampSlewPerFrame = seekedDistance / GainRamp.SeekCrossFadeFrames;
            Assert.Multiple(() =>
            {
                Assert.That(
                    Left(crossfade)[0],
                    Is.EqualTo(PositionOf(oldFrame)).Within(rampSlewPerFrame * 2f),
                    "the first frame after the seek is still the position that was playing");
                Assert.That(
                    afterCrossfade,
                    Is.EqualTo(PositionOf(newFrame)).Within(0.001f),
                    "and once the ramp is over only the seeked position is left");
                // Spread over the ramp, the seek moves the output by no more than a ramp's worth
                // per frame. Cutting instead of crossfading puts the whole distance into one step.
                Assert.That(
                    MixerProbe.MaximumStep(crossfade),
                    Is.LessThan(rampSlewPerFrame * 1.5f),
                    "the crossfade must not step: that step is the click the ramp exists to remove");
            });
        }

        // Media whose sample value states the frame it came from, so the output says which position
        // produced it. Scaled well below full scale so the sum of both sides stays linear.
        private static SourceMedia PositionRamp(int frameCount)
            => new(
                "ramp",
                Enumerable.Range(0, frameCount).SelectMany(frame => new[] { PositionOf(frame), PositionOf(frame) }).ToArray(),
                PlaybackFormat.ChannelCount,
                PlaybackFormat.SampleRate);

        private static float PositionOf(int frame) => 0.1f + frame / 100_000f;

        private static Shared.GameFormats.Wwise.Hirc.V112.CAkBus_V112 AttilaBus(uint id, uint outputBusId, float decibels)
        {
            var bus = new Shared.GameFormats.Wwise.Hirc.V112.CAkBus_V112 { Id = id, OverrideBusId = outputBusId };
            bus.BusInitialParams.AkPropBundle.PropsList.Add(new Shared.GameFormats.Wwise.Hirc.V112.Shared.AkPropBundle_V112.AkPropBundleInstance_V112
            {
                Id = Shared.GameFormats.Wwise.Enums.Enums_V112.AkPropId_V112.BusVolume,
                Value = BitConverter.SingleToUInt32Bits(decibels)
            });
            return bus;
        }

        private static CAkBus_V136 Bus(uint id, uint outputBusId, float decibels)
        {
            var bus = new CAkBus_V136 { Id = id, OverrideBusId = outputBusId };
            bus.BusInitialParams.AkPropBundle.PropsList.Add(new AkPropBundle_V136.PropBundleInstance_V136
            {
                Id = AkPropId_V136.BusVolume,
                Value = BitConverter.SingleToUInt32Bits(decibels)
            });
            return bus;
        }

        private static float[] Play(SourceMedia media, int frameCount)
        {
            var voices = new VoicePool(2, new EngineTelemetry());
            var playingId = new PlayingId(1, default);
            voices.Activate(VoiceStart.ForMedia(playingId, new PostCompletionState(playingId), media, VoiceParameters.Neutral, 0, false));
            return Mix(voices, frameCount);
        }

        private static float[] Mix(VoicePool voices, int frameCount)
        {
            var bus = new Bus(frameCount);
            bus.Clear(frameCount);
            voices.MixBlock(bus, 0, frameCount);
            return bus.Samples;
        }

        private static float[] Left(float[] interleaved)
            => Enumerable.Range(0, interleaved.Length / PlaybackFormat.ChannelCount)
                .Select(frame => interleaved[frame * PlaybackFormat.ChannelCount])
                .ToArray();

        private static float[] ResampleTone(double frequency)
        {
            const int sourceRate = 96_000;
            var samples = Enumerable.Range(0, 8_192)
                .Select(frame => MathF.Sin((float)(2d * Math.PI * frequency * frame / sourceRate)))
                .ToArray();
            var media = new SourceMedia(frequency.ToString(), samples, 1, sourceRate);
            var resampler = new Resampler();
            resampler.Start(media, 64d);
            var output = new float[2_000];
            for (var frame = 0; frame < output.Length; frame++)
            {
                output[frame] = resampler.ReadChannel(media, 0);
                resampler.Advance();
            }
            return output;
        }

        private static float RootMeanSquare(float[] samples)
            => MathF.Sqrt(samples.Sum(sample => sample * sample) / samples.Length);

        private static byte[] WaveWithLoop()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write("RIFF"u8);
            writer.Write(112u);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16u);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write(48_000u);
            writer.Write(96_000u);
            writer.Write((ushort)2);
            writer.Write((ushort)16);
            writer.Write("smpl"u8);
            writer.Write(60u);
            for (var field = 0; field < 7; field++)
                writer.Write(0u);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(1u);
            writer.Write(2u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write("data"u8);
            writer.Write(8u);
            writer.Write((short)0);
            writer.Write((short)1_000);
            writer.Write((short)2_000);
            writer.Write((short)3_000);
            return stream.ToArray();
        }
    }
}
