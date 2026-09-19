using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Output;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Rendering;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Moq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise;
using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc;
using System.Numerics;
using System.Reflection;

namespace Test.Audio
{
    // The checkpoints that have no instrument but a person, plus the numeric probes that decide
    // which of them are worth anybody listening to.
    //
    // All [Explicit]: they make noise or take seconds, and none runs in the normal suite. Run one
    // at a time:
    //   dotnet test Editors/Audio/Test.Audio.csproj --filter "FullyQualifiedName~SwitchCrossFade" --logger "console;verbosity=detailed"
    //
    // The attenuation listening checks that used to live here were removed on 2026-09-11. They
    // corrupted their own output -- peaks far above the source, hundreds of discontinuities, an
    // envelope that pulsed on a cycle nothing asked for -- while the same voice pool and provider
    // pulled by hand were exact. The cause was never found. InspectPositionedVoice below measures
    // the same thing without a sink and is what actually confirmed phase 15 curve scaling.
    [Explicit]
    public class ListeningChecks
    {
        [Test]
        public void AuditionInstalledWarhammerThreeWemsSequentially()
        {
            const int clipCount = 4;
            const int auditionMilliseconds = 2500;
            Report($"Playing {clipCount} installed Warhammer III WEMs sequentially, up to {auditionMilliseconds / 1000.0:F1} seconds each.");

            using var device = new AudioOutputDevice();
            using var engine = new SoundEngine(device);
            var mediaCache = new MediaCache(new Mock<Shared.Core.PackFiles.IPackFileService>().Object);
            var played = 0;
            foreach (var (path, bytes) in LoadStandaloneWems(FindWarhammerThreeAudioPack()))
            {
                SourceMedia media;
                try
                {
                    media = mediaCache.GetWem(bytes);
                }
                catch (Exception exception)
                {
                    Report($"  ... skipped {path}: {exception.Message}");
                    continue;
                }

                Report($"  ... {path} ({media.Duration.TotalSeconds:F2} seconds)");
                var playingId = engine.PlayMedia(media);
                Thread.Sleep(Math.Min(auditionMilliseconds, Math.Max(300, (int)media.Duration.TotalMilliseconds)));
                StopAndDrain(engine, playingId);
                if (++played == clipCount)
                    break;
            }

            Assert.That(played, Is.EqualTo(clipCount), "Expected to decode enough standalone WEMs for the listening check.");
        }


        // PHASE 15 CHANGE, and the reason the curve scaling matters. Before it, a volume curve that
        // runs to silence was read as running one decibel down, so distance did almost nothing.
        //
        // LISTEN FOR: a tone that fades smoothly to silence as the emitter recedes, then swells
        // back. Run AttenuationWithoutCurveScaling first for the comparison.
        //
        // Measured, so the ear has something to check rather than discover: scaled runs 1.000 at
        // the listener to 0.000 at full distance; unscaled only reaches 0.891. See
        // InspectPositionedVoice, which measures the same thing without a sink.
        [Test]
        public void AttenuationOverDistance()
        {
            Report("A 440 Hz tone on an emitter moving 0 to 100 units away and back, over 8 seconds.");
            Report("LISTEN FOR: an obvious fade to silence in the middle, and a swell back.");
            PlayPositioned(new WwiseCurve(AkCurveScaling.Decibels,
                [new WwiseCurvePoint(0f, 0f, 4), new WwiseCurvePoint(100f, -1f, 4)]), seconds: 8);
        }

        // The same movement with the curve left unscaled, which is how it used to be read. Run this
        // first: it is the baseline the one above is judged against.
        [Test]
        public void AttenuationWithoutCurveScaling()
        {
            Report("The same movement, with the curve unscaled -- how it was read before phase 15.");
            Report("LISTEN FOR: almost no change in level. This is the baseline, not the fix.");
            PlayPositioned(new WwiseCurve(AkCurveScaling.None,
                [new WwiseCurvePoint(0f, 0f, 4), new WwiseCurvePoint(100f, -1f, 4)]), seconds: 8);
        }

        // The one check that needs a pair of hands rather than a pair of ears.
        //
        // Plays a continuous tone for thirty seconds while the listener changes the default output
        // device, or unplugs the one being used. The engine is supposed to notice, rebuild its sink
        // on whatever the machine has moved to, and carry on -- and the playhead is supposed to
        // survive it, because a rebuilt device is a new clock starting at zero.
        //
        // LISTEN FOR: the tone stopping, and whether it comes back on the new device. A pause of a
        // second or so is expected; silence from then on is not.
        [Test]
        public void DeviceChangeMidPlayback()
        {
            Report("Playing a tone for 30 seconds.");
            Report("");
            Report("  PART WAY THROUGH: change your default playback device in Windows sound");
            Report("  settings, or unplug the one it is using.");
            Report("");
            Report("LISTEN FOR: whether the tone comes back on the new device, and how long it takes.");

            using var device = new AudioOutputDevice();
            using var engine = new SoundEngine(device);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("tone", 10)
                .WithSound(10, Tone(440, 40f)));
            var gameObject = engine.RegisterGameObject("probe");
            var playingId = engine.PostEvent("tone", gameObject);

            // Nothing to nudge: the engine polls the device position every twenty-five
            // milliseconds while anything is playing, and a rebuild happens off the back of that.
            var positions = new List<string>();
            for (var second = 0; second < 30; second++)
            {
                Thread.Sleep(1000);
                if (second % 5 == 4)
                    positions.Add($"{second + 1}s: playhead {engine.GetPosition(playingId)?.TotalSeconds:F1}");
            }

            Report("");
            Report($"    endpoint changes seen: {device.LastEndpointChangeReason}");
            Report($"    sink rebuilds: {device.RebuildCount}, failed rebuilds: {device.FailedRebuildCount}");
            Report($"    block overruns: {engine.Telemetry.BlockOverrunCount}");
            foreach (var position in positions)
                Report($"    {position}");
            StopAndDrain(engine, playingId);
        }

        // PHASE 15 CHANGE. Switch cross-fade times were read as floats where the bank stores
        // signed integer milliseconds, so every authored fade arrived as zero.
        //
        // LISTEN FOR: a two-second glide between two pitches. A hard, instant jump is the old
        // behaviour.
        [Test]
        public void SwitchCrossFadeTakesItsAuthoredTime()
        {
            Report("A 220 Hz tone, then a switch change to a 440 Hz tone with a 2000 ms authored cross-fade.");
            Report("LISTEN FOR: the two tones overlapping for about two seconds.");
            Report("An instant swap means the authored fade is arriving as zero.");

            var hierarchy = new FakeHierarchy()
                .WithEvent("engine", 10)
                .WithSwitchContainer(10, "gear", "low", ("low", [1]), ("high", [2]))
                .WithContinuousSwitchValidation(2000, 2000, 1, 2)
                .WithSound(1, Tone(220))
                .WithSound(2, Tone(440));

            using var device = new AudioOutputDevice();
            using var engine = new SoundEngine(device);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("gearbox");

            engine.PostEvent("engine", gameObject);
            Report("  ... low gear");
            Thread.Sleep(2000);
            Report("  ... changing to high");
            engine.SetSwitch("gear", "high", gameObject);
            Thread.Sleep(4000);
            StopAndDrain(engine);
        }

        // The limiter, at the levels banks actually author.
        //
        // The first version of this fired eight cues at 0.9 over a 0.2 bed, which drove the mix to
        // 7.3 times full scale and ducked the bed by 19 dB. It was seven times past anything in the
        // game and said nothing about normal playback. Phase 13 measured what banks contain: a
        // median accumulated gain of -13 dB down the actor-mixer hierarchy and bus chain, and the
        // five-voice peak the Throt animation really reached was 1.12x.
        //
        // So this plays the measured case. LISTEN FOR: whether the bed moves at all. It should be
        // close to inaudible -- about a decibel of reduction at the peak.
        [Test]
        public void LimiterAtAuthoredLevels()
        {
            Report("A bed with bursts of five cues at the median authored level, for ten seconds.");
            Report("This is what the game actually contains, not a deliberate slam.");
            Report("LISTEN FOR: the bed holding steady. Obvious ducking here would be a real problem.");

            PlayLimiterCheck(burstAmplitude: AuthoredMedianAmplitude, burstVoices: 5);
        }

        // The slam, kept for comparison rather than as the headline. Hearing the two next to each
        // other is what makes "the limiter only works when something extreme arrives" audible.
        [Test]
        public void LimiterWhenTheMixIsSlammed()
        {
            Report("The same bed with eight cues at near full scale -- far past anything a bank holds.");
            Report("LISTEN FOR: obvious ducking and swelling. This is the limiter doing its job.");

            PlayLimiterCheck(burstAmplitude: 0.9f, burstVoices: 8);
        }

        // Chosen so five of these over the bed peak at 1.12 times full scale before limiting, which
        // is what phase 13 measured the Throt animation's own five-voice peak to reach. Picking the
        // median authored gain instead put the peak at 1.31, which is hotter than the game gets and
        // made the limiter sound busier than it is.
        private const float AuthoredMedianAmplitude = 0.184f;

        private static void PlayLimiterCheck(float burstAmplitude, int burstVoices)
        {
            var hierarchy = new FakeHierarchy()
                .WithEvent("bed", 10)
                .WithSound(10, Tone(220, 11f, amplitude: 0.2f))
                .WithEvent("burst", 20)
                .WithSound(20, Tone(660, 0.4f, amplitude: burstAmplitude));

            using var device = new AudioOutputDevice();
            using var engine = new SoundEngine(device);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("mix");

            engine.PostEvent("bed", gameObject);
            for (var second = 0; second < 10; second++)
            {
                Thread.Sleep(1000);
                for (var voice = 0; voice < burstVoices; voice++)
                    engine.PostEvent("burst", gameObject);
            }
            Report($"Master peak before limiting: {engine.Telemetry.MasterPeakLevel:F2}");
            StopAndDrain(engine);
        }

        // Owed since phase 13, which changed it. A non-looping timeline used to deactivate its
        // voices at the end; it now ramps them out over the final block.
        //
        // LISTEN FOR: the tone ending smoothly. A click or a hard chop is the old behaviour.
        [Test]
        public void NonLoopingTimelineEndsWithoutAClick()
        {
            Report("A tone on a three-second non-looping timeline, played to its end, three times over.");
            Report("LISTEN FOR: a clean ending each time. A click or hard chop is a fault.");

            var hierarchy = new FakeHierarchy()
                .WithEvent("cue", 10)
                .WithSound(10, Tone(330, 10f));

            using var device = new AudioOutputDevice();
            using var engine = new SoundEngine(device);
            engine.LoadHierarchy(hierarchy);
            var gameObject = engine.RegisterGameObject("timeline");

            for (var pass = 0; pass < 3; pass++)
            {
                var timeline = engine.CreateTimeline(TimeSpan.FromSeconds(3), shouldLoop: false);
                engine.ScheduleEvent("cue", gameObject, TimeSpan.Zero, timeline);
                engine.Start(timeline);
                Report($"  ... pass {pass + 1}");
                Thread.Sleep(4000);
            }
            StopAndDrain(engine);
        }

        // Owed since phase 13. A mid-playback seek starts a second voice at the new position with a
        // forced fade-in and ramps the old one out.
        //
        // LISTEN FOR: each jump crossfading rather than clicking.
        [Test]
        public void SeekingMidPlaybackCrossFades()
        {
            Report("A ten-second tone, seeked to a new position every second.");
            Report("LISTEN FOR: smooth jumps. Clicks at each seek are a fault.");

            using var device = new AudioOutputDevice();
            using var engine = new SoundEngine(device);
            var playingId = engine.PlayMedia(Tone(440, 10f), shouldLoop: true);

            var positions = new Random(11);
            for (var seek = 0; seek < 10; seek++)
            {
                Thread.Sleep(1000);
                var position = TimeSpan.FromSeconds(positions.NextDouble() * 8);
                Report($"  ... seeking to {position.TotalSeconds:F2} s");
                engine.Seek(playingId, position);
            }
            StopAndDrain(engine);
        }

        // THE ACTUAL REPRODUCTION. Configuration 4 of the sink matrix plays a clean tone when it is
        // fed a signal generator, which says the sink configuration is not the fault on its own.
        // What originally failed was the engine feeding that sink -- and the engine has been
        // rewritten since, so the sink swap may have worked for the wrong reason.
        //
        // LISTEN FOR: the same clean tone the matrix produced. Noise here, with the matrix clean,
        // would mean the fault is in what the engine hands the sink rather than in the sink.
        [Test]
        public void EngineThroughWasapiSharedEventFloat()
        {
            Report("The engine itself, playing a 440 Hz tone through WasapiOut shared, event sync, 32-bit float.");
            Report("This is the combination that originally played noise -- but with today's engine.");
            Report("LISTEN FOR: a clean steady tone for six seconds.");

            using var device = new WasapiOutputDevice(AudioClientShareMode.Shared, useEventSync: true, latencyMilliseconds: 20);
            using var engine = new SoundEngine(device);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("tone", 10)
                .WithSound(10, Tone(440, 8f)));
            var gameObject = engine.RegisterGameObject("probe");

            engine.PostEvent("tone", gameObject);
            Thread.Sleep(6000);
            Report($"Block overruns during the run: {engine.Telemetry.BlockOverrunCount}");
        }

        // The same, with the engine's mix converted to 16-bit before it reaches WASAPI, so the one
        // variable between them is the sample format the sink is handed.
        [Test]
        public void EngineThroughWasapiSharedEventPcm()
        {
            Report("The same, but handing WASAPI 16-bit PCM instead of float.");
            Report("LISTEN FOR: a clean steady tone for six seconds.");

            using var device = new WasapiOutputDevice(AudioClientShareMode.Shared, useEventSync: true, latencyMilliseconds: 20, useFloat: false);
            using var engine = new SoundEngine(device);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("tone", 10)
                .WithSound(10, Tone(440, 8f)));
            var gameObject = engine.RegisterGameObject("probe");

            engine.PostEvent("tone", gameObject);
            Thread.Sleep(6000);
            Report($"Block overruns during the run: {engine.Telemetry.BlockOverrunCount}");
        }

        // No listening. Records exactly what the sink asks the engine for and exactly what the
        // engine hands back, in both formats, because the two differ in the one path that
        // crackles. Two candidates and this tells them apart:
        //   odd requested sample counts -- the engine renders whole frames, so an odd count leaves
        //   its last sample silent, which is a click per callback;
        //   samples outside [-1, 1] -- harmless once converted to 16-bit, because that conversion
        //   clamps, but handed to WASAPI as raw float they are out of range.
        [TestCase(true, TestName = "InspectEngineOutput_Float")]
        [TestCase(false, TestName = "InspectEngineOutput_Pcm16")]
        public void InspectEngineOutput(bool useFloat)
        {
            var requestedCounts = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();
            var oddRequests = 0;
            var peak = 0f;
            var outOfRange = 0;
            var nonFinite = 0;

            using var device = new WasapiOutputDevice(AudioClientShareMode.Shared, true, 20, useFloat);
            using var engine = new SoundEngine(device);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("tone", 10)
                .WithSound(10, Tone(440, 8f)));
            var gameObject = engine.RegisterGameObject("probe");

            device.Inspect = (buffer, offset, count) =>
            {
                requestedCounts.AddOrUpdate(count, 1, (_, existing) => existing + 1);
                if (count % PlaybackFormat.ChannelCount != 0)
                    Interlocked.Increment(ref oddRequests);
                for (var index = offset; index < offset + count; index++)
                {
                    var sample = buffer[index];
                    if (!float.IsFinite(sample)) { nonFinite++; continue; }
                    var magnitude = Math.Abs(sample);
                    if (magnitude > peak) peak = magnitude;
                    if (magnitude > 1f) outOfRange++;
                }
            };

            var captured = new List<float>();
            device.Inspect = (buffer, offset, count) =>
            {
                requestedCounts.AddOrUpdate(count, 1, (_, existing) => existing + 1);
                if (count % PlaybackFormat.ChannelCount != 0)
                    Interlocked.Increment(ref oddRequests);
                for (var index = offset; index < offset + count; index++)
                {
                    var sample = buffer[index];
                    if (!float.IsFinite(sample)) { nonFinite++; continue; }
                    var magnitude = Math.Abs(sample);
                    if (magnitude > peak) peak = magnitude;
                    if (magnitude > 1f) outOfRange++;
                    if (captured.Count < PlaybackFormat.SampleRate * PlaybackFormat.ChannelCount * 2)
                        captured.Add(sample);
                }
            };

            engine.PostEvent("tone", gameObject);
            Thread.Sleep(4000);

            var capturePath = Path.Combine(Path.GetTempPath(), $"engine-output-{(useFloat ? "float" : "pcm16")}.raw");
            using (var writer = new BinaryWriter(File.Create(capturePath)))
            {
                foreach (var sample in captured)
                    writer.Write(sample);
            }
            Report($"    captured {captured.Count} samples to {capturePath}");

            Report($"--- {(useFloat ? "float" : "16-bit PCM")}");
            Report($"    requested sample counts: {string.Join(", ", requestedCounts.OrderBy(p => p.Key).Select(p => $"{p.Key} x{p.Value}"))}");
            Report($"    requests not a whole number of frames: {oddRequests}");
            Report($"    peak sample magnitude: {peak:F6}");
            Report($"    samples outside [-1, 1]: {outOfRange}");
            Report($"    non-finite samples: {nonFinite}");
            Report($"    block overruns: {engine.Telemetry.BlockOverrunCount}");
            Report($"    {device.ThreadReport}");
        }

        // No sound. Seeks at known points and measures the waveform across each one, so "a click on
        // most jumps but not all" becomes a number per jump rather than an impression.
        //
        // The question is whether the cross-fade is failing outright on some seeks, or whether it
        // is working everywhere and simply cannot hide a phase discontinuity in a pure sine over a
        // ramp this short. Those want different fixes.
        [Test]
        public void InspectSeekDiscontinuities()
        {
            var output = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(output);
            var playingId = engine.PlayMedia(Tone(440, 20f), shouldLoop: true);

            var seekTargets = new[] { 3.78, 0.36, 3.66, 7.25, 1.38, 1.37, 6.68, 7.69, 2.71, 2.64 };
            Report("    seek to | largest step across the seek | verdict");
            foreach (var target in seekTargets)
            {
                // Settle, so what is measured is the seek and not the previous one.
                output.Render(4_800);

                // Confirm the seek actually moved the playhead. A probe that measures a seek which
                // never happened reads perfectly clean and means nothing.
                output.DevicePositionFrames += 4_800;
                var positionBefore = engine.GetPosition(playingId);
                engine.Seek(playingId, TimeSpan.FromSeconds(target));

                var rendered = output.Render(4_800);
                output.DevicePositionFrames += 4_800;
                var positionAfter = engine.GetPosition(playingId);
                var largestStep = 0f;
                var atFrame = 0;
                for (var frame = 1; frame < rendered.Length / PlaybackFormat.ChannelCount; frame++)
                {
                    var step = Math.Abs(
                        rendered[frame * PlaybackFormat.ChannelCount]
                        - rendered[(frame - 1) * PlaybackFormat.ChannelCount]);
                    if (step > largestStep)
                    {
                        largestStep = step;
                        atFrame = frame;
                    }
                }

                // A 440 Hz sine at 48 kHz moves at most 0.029 per sample. Anything far above that
                // is a discontinuity the ear will hear as a click.
                // A step catches a click. It does not catch what cross-fading two sines at
                // different phases does: they cancel or reinforce, so the level dips or swells over
                // the ramp with no discontinuity anywhere. That is heard as a bump, so measure the
                // envelope over the ramp as well.
                var rampFrames = GainRamp.DeclickFrames * 2;
                var beforeLevel = PeakOver(rendered, 0, 480);
                var duringLevel = PeakOver(rendered, 480, rampFrames);
                var afterLevel = PeakOver(rendered, 480 + rampFrames, 480);
                var steady = Math.Max(beforeLevel, afterLevel);
                var excursion = steady <= 0f ? 0f : (duringLevel - steady) / steady;

                var verdict = largestStep > 0.1f ? "CLICK"
                    : Math.Abs(excursion) > 0.15f ? "LEVEL EXCURSION"
                    : "clean";
                // The one thing a step and a level cannot see. Cross-fading two points of the same
                // sine has to slew the phase from one to the other, which is a brief frequency
                // deviation: inaudible on broadband audio, a "bump" on a pure tone. Measured as the
                // shortest and longest gap between rising zero crossings -- a steady 440 Hz sine
                // crosses every 109 frames.
                var crossings = new List<int>();
                for (var frame = 1; frame < rendered.Length / PlaybackFormat.ChannelCount; frame++)
                {
                    var previous = rendered[(frame - 1) * PlaybackFormat.ChannelCount];
                    var current = rendered[frame * PlaybackFormat.ChannelCount];
                    if (previous <= 0f && current > 0f)
                        crossings.Add(frame);
                }
                var shortest = int.MaxValue;
                var longest = 0;
                for (var index = 1; index < crossings.Count; index++)
                {
                    var gap = crossings[index] - crossings[index - 1];
                    shortest = Math.Min(shortest, gap);
                    longest = Math.Max(longest, gap);
                }
                var deviated = shortest < 100 || longest > 120;
                Report($"    {target,7:F2} | playhead {positionBefore?.TotalSeconds,6:F2} -> {positionAfter?.TotalSeconds,6:F2} | step {largestStep:F5} | cycle {shortest,3}-{longest,3} frames (440 Hz = 109) | {(deviated ? "PHASE SLEW" : "steady")}");
            }
        }

        private static float PeakOver(float[] interleaved, int firstFrame, int frameCount)
        {
            var peak = 0f;
            var lastFrame = Math.Min(firstFrame + frameCount, interleaved.Length / PlaybackFormat.ChannelCount);
            for (var frame = firstFrame; frame < lastFrame; frame++)
                peak = Math.Max(peak, Math.Abs(interleaved[frame * PlaybackFormat.ChannelCount]));
            return peak;
        }

        // No sound. How deep the pumping actually is, and whether it happens at levels the game
        // authors rather than only when the mix is deliberately slammed.
        //
        // The listening check uses eight cues at 0.9 over a 0.2 bed, which is far past anything a
        // bank contains: phase 13 measured the gain a sound accumulates down the hierarchy and the
        // bus chain at a median of -13 dB. So it is run twice -- once slammed, once at the measured
        // median -- and the difference between them is the point.
        [TestCase(0.9f, 8, TestName = "InspectLimiterPumping_Slammed")]
        [TestCase(0.184f, 5, TestName = "InspectLimiterPumping_AtAuthoredLevels")]
        public void InspectLimiterPumping(float burstAmplitude, int burstVoices)
        {
            var output = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(output);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("bed", 10)
                .WithSound(10, Tone(220, 10f, amplitude: 0.2f))
                .WithEvent("burst", 20)
                .WithSound(20, Tone(660, 0.4f, amplitude: burstAmplitude)));
            var gameObject = engine.RegisterGameObject("mix");

            engine.PostEvent("bed", gameObject);
            var beforeBurst = BandLevel(output.Render(4_800), 220);

            for (var voice = 0; voice < burstVoices; voice++)
                engine.PostEvent("burst", gameObject);

            var duringBurst = BandLevel(output.Render(4_800), 220);
            // The bursts are 0.4 s; give them time to finish and the limiter time to release.
            output.Render(48_000);
            var afterBurst = BandLevel(output.Render(4_800), 220);

            var duckDecibels = 20 * Math.Log10(Math.Max(duringBurst, 1e-9) / Math.Max(beforeBurst, 1e-9));
            Report($"--- {burstVoices} bursts at {burstAmplitude}");
            Report($"    bed level before {beforeBurst:F4}, during {duringBurst:F4}, after {afterBurst:F4}");
            Report($"    the bed is ducked by {duckDecibels:F1} dB while the bursts sound");
            Report($"    master peak before limiting: {engine.Telemetry.MasterPeakLevel:F2}");
        }

        // Correlates against a 220 Hz sine, so the bed can be measured underneath the 660 Hz bursts
        // instead of the two being summed into one meaningless peak.
        private static float BandLevel(float[] interleaved, double hertz)
        {
            var frameCount = interleaved.Length / PlaybackFormat.ChannelCount;
            double real = 0, imaginary = 0;
            for (var frame = 0; frame < frameCount; frame++)
            {
                var sample = interleaved[frame * PlaybackFormat.ChannelCount];
                var angle = 2 * Math.PI * hertz * frame / PlaybackFormat.SampleRate;
                real += sample * Math.Cos(angle);
                imaginary += sample * Math.Sin(angle);
            }
            return (float)(2 * Math.Sqrt(real * real + imaginary * imaginary) / frameCount);
        }

        // No sound. Which action types leave bytes behind, and how many, so the phase that fixes
        // them starts from a list rather than a total.
        [Test]
        public void InspectActionUnreadBytes()
        {
            foreach (var (corpus, bankVersion) in new[] { ("corpus_v136", 2147483784u), ("corpus_v112", 112u) })
            {
                var directory = Path.Combine(Path.GetTempPath(), corpus);
                if (!Directory.Exists(directory))
                {
                    Report($"--- {corpus}: not extracted on this machine");
                    continue;
                }

                var byActionType = new Dictionary<string, (int Total, int Unread, int WorstBytes)>();
                foreach (var bankPath in Directory.GetFiles(directory, "*.bnk"))
                {
                    foreach (var (bytes, _, type) in HircsIn(bankPath))
                    {
                        if (type != (byte)Shared.GameFormats.Wwise.Enums.AkBkHircType.Action)
                            continue;
                        Shared.GameFormats.Wwise.Hirc.HircItem hirc;
                        try { hirc = Shared.GameFormats.Wwise.Hirc.HircItem.ReadData(bankPath, new ByteChunk(bytes), bankVersion, 0, true, 0); }
                        catch { continue; }

                        var actionType = hirc switch
                        {
                            Shared.GameFormats.Wwise.Hirc.V136.CAkAction_V136 a => a.ActionType.ToString(),
                            Shared.GameFormats.Wwise.Hirc.V112.CAkAction_V112 b => b.ActionType.ToString(),
                            _ => "unknown"
                        };
                        var entry = byActionType.GetValueOrDefault(actionType);
                        byActionType[actionType] = (
                            entry.Total + 1,
                            entry.Unread + (hirc.UnreadByteCount != 0 ? 1 : 0),
                            hirc.UnreadByteCount != 0 && Math.Abs(hirc.UnreadByteCount) > Math.Abs(entry.WorstBytes)
                                ? hirc.UnreadByteCount
                                : entry.WorstBytes);
                    }
                }

                Report($"--- {corpus}");
                foreach (var pair in byActionType.OrderByDescending(p => p.Value.Unread))
                    Report($"    {pair.Key,-24} {pair.Value.Total,6} actions, {pair.Value.Unread,5} leaving bytes, worst {pair.Value.WorstBytes}");
            }
        }

        private static IEnumerable<(byte[] Bytes, uint Id, byte Type)> HircsIn(string bankPath)
        {
            var data = File.ReadAllBytes(bankPath);
            var position = 0;
            while (position + 8 <= data.Length)
            {
                var tag = System.Text.Encoding.ASCII.GetString(data, position, 4);
                var size = BitConverter.ToUInt32(data, position + 4);
                if (tag == "HIRC")
                {
                    var itemCount = BitConverter.ToUInt32(data, position + 8);
                    var itemPosition = position + 12;
                    for (var item = 0; item < itemCount && itemPosition + 5 <= data.Length; item++)
                    {
                        var type = data[itemPosition];
                        var sectionSize = BitConverter.ToUInt32(data, itemPosition + 1);
                        var total = (int)sectionSize + 5;
                        if (itemPosition + total > data.Length) break;
                        yield return (data[itemPosition..(itemPosition + total)], BitConverter.ToUInt32(data, itemPosition + 5), type);
                        itemPosition += total;
                    }
                }
                position += 8 + (int)size;
            }
        }

        // No sound, no device. Pulls 960-sample buffers -- the size WASAPI asks for -- straight out
        // of the engine and checks them against the sine that went in. If the corruption appears
        // here it is the engine alone, and WASAPI is a bystander.
        [TestCase(960, TestName = "EngineOutputIsContinuous_960SampleBuffers")]
        [TestCase(1920, TestName = "EngineOutputIsContinuous_1920SampleBuffers")]
        [TestCase(882, TestName = "EngineOutputIsContinuous_882SampleBuffers")]
        public void EngineOutputIsContinuous(int samplesPerPull)
        {
            var output = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(output);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("tone", 10)
                .WithSound(10, Tone(440, 8f)));
            var gameObject = engine.RegisterGameObject("probe");
            engine.PostEvent("tone", gameObject);

            var rendered = new List<float>();
            for (var pull = 0; pull < 200; pull++)
                rendered.AddRange(output.Render(samplesPerPull / PlaybackFormat.ChannelCount));

            var left = new List<float>();
            for (var index = 0; index < rendered.Count; index += PlaybackFormat.ChannelCount)
                left.Add(rendered[index]);

            var peak = left.Max(Math.Abs);
            var largestStep = 0f;
            var discontinuities = 0;
            for (var frame = 1; frame < left.Count; frame++)
            {
                var step = Math.Abs(left[frame] - left[frame - 1]);
                if (step > largestStep) largestStep = step;
                if (step > 0.1f) discontinuities++;
            }

            Report($"--- {samplesPerPull}-sample pulls, {left.Count} frames rendered");
            Report($"    peak {peak:F6}  (the source tone is 0.5)");
            Report($"    largest sample-to-sample step {largestStep:F6}  (a 440 Hz sine moves 0.029 per sample)");
            Report($"    discontinuities over 0.1: {discontinuities}");
        }

        // No sound. Renders the same positioned voice the attenuation checks play, and reports the
        // level envelope over distance, so the harness can be judged without anybody listening.
        [TestCase(true, TestName = "InspectPositionedVoice_Scaled")]
        [TestCase(false, TestName = "InspectPositionedVoice_Unscaled")]
        public void InspectPositionedVoice(bool useDecibelScaling)
        {
            var curve = new WwiseCurve(
                useDecibelScaling ? AkCurveScaling.Decibels : AkCurveScaling.None,
                [new WwiseCurvePoint(0f, 0f, 4), new WwiseCurvePoint(100f, -1f, 4)]);

            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(4, telemetry);
            var parameters = new GameObjectParameterSource();
            parameters.Publish(new GameObjectParameters([], Vector3.Zero, [Vector3.Zero]));
            var playingId = new PlayingId(1, default);
            voices.Activate(VoiceStart.ForMedia(
                playingId, new PostCompletionState(playingId), Tone(440, 30f),
                VoiceParameters.Neutral, 0, isLooping: true) with
            {
                Attenuation = new AttenuationSettings(curve, WwiseCurve.Empty, WwiseCurve.Empty),
                GameObjectParameters = parameters,
                IsPositioned = true
            });

            var bus = new Bus(512);
            var absoluteFrame = 0L;
            Report($"--- {(useDecibelScaling ? "decibel-scaled" : "unscaled")} curve");
            Report("    distance | peak in that step | continuous?");
            for (var step = 0; step <= 10; step++)
            {
                var distance = step * 10f;
                // Along Z, not X: the panner derives its pan from relative.X, so moving sideways
                // sends the voice hard into one channel and distance stops being the variable.
                parameters.Publish(new GameObjectParameters([], new Vector3(0f, 0f, distance), [Vector3.Zero]));

                var peak = 0f;
                var largestStep = 0f;
                var previous = 0f;
                for (var block = 0; block < 20; block++)
                {
                    bus.Clear(512);
                    voices.MixBlock(bus, absoluteFrame, 512);
                    absoluteFrame += 512;
                    for (var frame = 0; frame < 512; frame++)
                    {
                        var sample = bus.Samples[frame * PlaybackFormat.ChannelCount]
                            + bus.Samples[frame * PlaybackFormat.ChannelCount + 1];
                        peak = Math.Max(peak, Math.Abs(sample));
                        if (block > 0 || frame > 0)
                            largestStep = Math.Max(largestStep, Math.Abs(sample - previous));
                        previous = sample;
                    }
                }
                Report($"    {distance,7:F0} | {peak:F6} | largest step {largestStep:F4}{(largestStep > 0.1f ? "  <-- DISCONTINUOUS" : "")}");
            }
        }

        // Ramps whatever is sounding down and lets it drain before the device is disposed.
        //
        // AudioOutputDevice.Dispose stops the sink outright, so tearing it down while a voice is at
        // full level cuts the waveform off and is heard as a click. That is nothing to do with what
        // any of these checks is demonstrating, and it masked a real question twice before being
        // recognised. Stop() ramps; the wait lets the ramp reach the speakers.
        private static void StopAndDrain(SoundEngine engine, PlayingId immediatePost = default)
        {
            // Stop() is the scheduled timeline only -- phase 10 made playback independently owned,
            // so an immediate post has to be stopped by its own identity. Missing that left a tone
            // sounding through the drain and clicking when the device was torn down.
            engine.Stop();
            if (immediatePost != default)
                engine.StopPlayingId(immediatePost);
            Thread.Sleep(300);
        }

        // Attenuation has no path through FakeHierarchy -- a sound reaches it through an
        // AttenuationId in its property bundle, which the fake cannot author -- so this drives the
        // voice pool directly.
        //
        // It plays through the production AudioOutputDevice on purpose. That type wraps its source
        // in SampleToWaveProvider16, which is what keeps it off the float path; handing an
        // ISampleProvider straight to an NAudio sink converts to 32-bit float and destroys the
        // signal. An earlier version of this harness did exactly that, and the pulsing and
        // crackling it produced were blamed on the engine for most of a session.
        private static void PlayPositioned(WwiseCurve volumeCurve, int seconds)
        {
            var telemetry = new EngineTelemetry();
            var voices = new VoicePool(4, telemetry);
            var parameters = new GameObjectParameterSource();
            parameters.Publish(new GameObjectParameters([], Vector3.Zero, [Vector3.Zero]));

            var playingId = new PlayingId(1, default);
            voices.Activate(VoiceStart.ForMedia(
                playingId,
                new PostCompletionState(playingId),
                Tone(440, seconds + 1f),
                VoiceParameters.Neutral,
                0,
                isLooping: true) with
            {
                Attenuation = new AttenuationSettings(volumeCurve, WwiseCurve.Empty, WwiseCurve.Empty),
                GameObjectParameters = parameters,
                IsPositioned = true
            });

            using var device = new AudioOutputDevice();
            device.EnsureCreated(new VoicePoolSampleProvider(voices));
            device.EnsurePlaying();

            var started = DateTime.UtcNow;
            while ((DateTime.UtcNow - started).TotalSeconds < seconds)
            {
                var progress = (DateTime.UtcNow - started).TotalSeconds / seconds;
                var distance = (float)(Math.Sin(progress * Math.PI * 2 - Math.PI / 2) + 1) / 2f * 100f;

                // Along Z. The panner takes its pan from relative.X, so moving sideways sends the
                // voice hard into one channel and swamps the level change this is demonstrating.
                parameters.Publish(new GameObjectParameters([], new Vector3(0f, 0f, distance), [Vector3.Zero]));
                Thread.Sleep(20);
            }

            // Leave the emitter at full distance and let it go quiet before the device is torn
            // down. Disposing the sink mid-waveform cuts it off at whatever level it was at, which
            // is heard as a click and has nothing to do with what this test is demonstrating.
            parameters.Publish(new GameObjectParameters([], new Vector3(0f, 0f, 100f), [Vector3.Zero]));
            Thread.Sleep(300);
        }

        // Pulls blocks straight out of the voice pool. The renderer is not involved, because what
        // is being listened to is one voice's own attenuation and panner.
        private sealed class VoicePoolSampleProvider(VoicePool voices) : ISampleProvider
        {
            private readonly Bus _bus = new(512);
            private long _absoluteOutputFrame;

            public WaveFormat WaveFormat { get; } =
                WaveFormat.CreateIeeeFloatWaveFormat(PlaybackFormat.SampleRate, PlaybackFormat.ChannelCount);

            public int Read(float[] buffer, int offset, int count)
            {
                Array.Clear(buffer, offset, count);
                var frameCount = count / PlaybackFormat.ChannelCount;
                var rendered = 0;
                while (rendered < frameCount)
                {
                    var blockFrames = Math.Min(512, frameCount - rendered);
                    _bus.Clear(blockFrames);
                    voices.MixBlock(_bus, _absoluteOutputFrame + rendered, blockFrames);
                    _bus.MixInto(buffer, offset + rendered * PlaybackFormat.ChannelCount, blockFrames);
                    rendered += blockFrames;
                }
                _absoluteOutputFrame += frameCount;
                return count;
            }
        }

        private static SourceMedia Tone(double hertz, float seconds = 4f, float amplitude = 0.5f)
        {
            var frameCount = (int)(PlaybackFormat.SampleRate * seconds);
            var samples = new float[frameCount * PlaybackFormat.ChannelCount];
            for (var frame = 0; frame < frameCount; frame++)
            {
                var value = amplitude * MathF.Sin((float)(2 * Math.PI * hertz * frame / PlaybackFormat.SampleRate));
                samples[frame * PlaybackFormat.ChannelCount] = value;
                samples[frame * PlaybackFormat.ChannelCount + 1] = value;
            }
            return MixerProbe.ToMedia(samples);
        }

        private static void Report(string line) => TestContext.Out.WriteLine(line);

        private static IEnumerable<(string Path, byte[] Bytes)> LoadStandaloneWems(string packPath)
        {
            using var stream = File.OpenRead(packPath);
            using var reader = new BinaryReader(stream);
            var loader = typeof(PackFileVersionConverter).Assembly
                .GetType("Shared.Core.PackFiles.Serialization.PackFileSerializerLoader", throwOnError: true)!;
            var load = loader.GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)!;
            var pack = (IPackFileContainer)load.Invoke(null, [packPath, stream.Length, reader, new CaPackDuplicateFileResolver(), GameTypeEnum.Warhammer3])!;
            foreach (var (path, file) in pack.GetAllFiles()
                .Where(entry => entry.Key.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                yield return (path, file.DataSource.ReadData());
            }
        }

        private static string FindWarhammerThreeAudioPack()
        {
            var candidates = new[]
            {
                @"D:\SteamLibrary\steamapps\common\Total War WARHAMMER III\data\audio_base.pack",
                @"C:\Program Files (x86)\Steam\steamapps\common\Total War WARHAMMER III\data\audio_base.pack",
                @"C:\Program Files\Steam\steamapps\common\Total War WARHAMMER III\data\audio_base.pack"
            };
            return candidates.FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException("Could not find Warhammer III audio_base.pack.");
        }

        // A WASAPI sink shaped like the engine's own output device, so the engine can be pointed at
        // it without changing anything that ships. Test-only on purpose: nothing is restored in
        // production until the listening says which configuration is sound.
        private sealed class WasapiOutputDevice(
            AudioClientShareMode shareMode,
            bool useEventSync,
            int latencyMilliseconds,
            bool useFloat = true) : IAudioOutputDevice
        {
            private WasapiOut _player;
            private InspectingSampleProvider _inspected;

            public string ThreadReport => _inspected == null
                ? "no reads"
                : $"threads {_inspected.CallingThreads}, overlapping reads {_inspected.ConcurrentReads}";

            // Set before playback starts to watch every buffer the sink pulls.
            public Action<float[], int, int> Inspect { get; set; }

            public bool EnsureCreated(ISampleProvider audioSampleSource)
            {
                if (_player != null)
                    return false;
                _player = new WasapiOut(shareMode, useEventSync, latencyMilliseconds);
                var inspected = new InspectingSampleProvider(audioSampleSource, (b, o, c) => Inspect?.Invoke(b, o, c));
                _inspected = inspected;
                if (useFloat)
                    _player.Init(inspected);
                else
                    _player.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider16(inspected));
                TestContext.Out.WriteLine($"    negotiated {_player.OutputWaveFormat.SampleRate} Hz, " +
                    $"{_player.OutputWaveFormat.Channels} ch, {_player.OutputWaveFormat.BitsPerSample}-bit {_player.OutputWaveFormat.Encoding}");
                return true;
            }

            public bool EnsurePlaying()
            {
                if (_player == null || _player.PlaybackState == PlaybackState.Playing)
                    return false;
                _player.Play();
                return true;
            }

            public AudioDevicePosition? CaptureOutputPosition()
            {
                if (_player == null)
                    return null;
                var bytesPerSecond = _player.OutputWaveFormat.AverageBytesPerSecond;
                return bytesPerSecond <= 0 ? null : new AudioDevicePosition(_player.GetPosition(), bytesPerSecond);
            }

            public void Dispose()
            {
                _player?.Stop();
                _player?.Dispose();
                _player = null;
            }
        }

        // Sits between the engine and the sink and reports each buffer as it passes.
        private sealed class InspectingSampleProvider(ISampleProvider source, Action<float[], int, int> inspect) : ISampleProvider
        {
            public WaveFormat WaveFormat => source.WaveFormat;

            private int _insideRead;
            private readonly HashSet<int> _callingThreads = [];

            public int ConcurrentReads { get; private set; }
            public string CallingThreads => string.Join(", ", _callingThreads);

            public int Read(float[] buffer, int offset, int count)
            {
                if (Interlocked.Exchange(ref _insideRead, 1) == 1)
                    ConcurrentReads++;
                lock (_callingThreads)
                    _callingThreads.Add(Environment.CurrentManagedThreadId);
                var read = source.Read(buffer, offset, count);
                inspect(buffer, offset, read);
                Interlocked.Exchange(ref _insideRead, 0);
                return read;
            }
        }

    }
}
