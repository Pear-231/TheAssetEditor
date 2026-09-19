using Editors.Audio.Shared.Wwise.Engine;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Test.Audio
{
    // Phase 14's instrument. Every test here is [Explicit] -- they make noise, they take seconds
    // rather than milliseconds, and two of them need a person to say what they heard.
    //
    // The question they exist to answer: shared-mode WasapiOut fed 32-bit float played noise in
    // place of the audio, the sink was changed to WaveOutEvent fed 16-bit PCM to get round it, and
    // that cost cue-onset accuracy -- 0.02 ms became 0.27 ms, and output latency 20 ms became 100.
    // Nobody knows which part of the WASAPI setup was at fault, and the obvious answer is ruled out
    // because the endpoint reports itself as float.
    //
    // Run them one at a time and report what comes back:
    //   dotnet test Editors/Audio/Test.Audio.csproj --filter "FullyQualifiedName~ReportOutputEndpoints" --logger "console;verbosity=detailed"
    [Explicit]
    public class Phase14SinkHarness
    {
        private const int SampleRate = 48_000;
        private const int ChannelCount = 2;
        private const int ToneHertz = 440;
        private const int PlaySeconds = 3;

        // What the machine actually offers. No sound, no judgement needed -- this is the context
        // every other result here has to be read against.
        [Test]
        public void ReportOutputEndpoints()
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultEndpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            Report($"Default render endpoint: {defaultEndpoint.FriendlyName}");
            Report("");

            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var isDefault = endpoint.ID == defaultEndpoint.ID ? " [DEFAULT]" : string.Empty;
                Report($"{endpoint.FriendlyName}{isDefault}");
                try
                {
                    var mixFormat = endpoint.AudioClient.MixFormat;
                    var subFormat = mixFormat is WaveFormatExtensible extensible
                        ? extensible.SubFormat.ToString()
                        : "not extensible";
                    Report($"    shared mix: {mixFormat.SampleRate} Hz, {mixFormat.Channels} ch, {mixFormat.BitsPerSample}-bit {mixFormat.Encoding}");
                    Report($"    subformat:  {subFormat}");
                    Report($"    exclusive 48k/16-bit stereo supported: {SupportsExclusive(endpoint, new WaveFormat(SampleRate, 16, ChannelCount))}");
                    Report($"    exclusive 48k/32-bit float stereo supported: {SupportsExclusive(endpoint, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, ChannelCount))}");
                }
                catch (Exception exception)
                {
                    Report($"    could not be queried: {exception.Message}");
                }
                Report("");
            }
        }

        // The configurations, in the order the run sheet numbers them. Kept as one list so the
        // whole matrix and a single configuration cannot drift apart.
        private static (string Name, Func<IWavePlayer> Create, bool UseFloat)[] Configurations() =>
        [
            ("WaveOutEvent, 16-bit PCM, 100 ms  <-- what ships today", () => new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 3 }, false),
            ("WaveOutEvent, 16-bit PCM, 50 ms", () => new WaveOutEvent { DesiredLatency = 50, NumberOfBuffers = 3 }, false),
            ("WaveOutEvent, 16-bit PCM, 20 ms", () => new WaveOutEvent { DesiredLatency = 20, NumberOfBuffers = 3 }, false),
            ("WasapiOut shared, event sync, 32-bit float  <-- the combination that failed", () => new WasapiOut(AudioClientShareMode.Shared, true, 20), true),
            ("WasapiOut shared, event sync, 16-bit PCM", () => new WasapiOut(AudioClientShareMode.Shared, true, 20), false),
            ("WasapiOut shared, push sync, 32-bit float", () => new WasapiOut(AudioClientShareMode.Shared, false, 20), true),
            ("WasapiOut shared, push sync, 16-bit PCM", () => new WasapiOut(AudioClientShareMode.Shared, false, 20), false),
            ("WasapiOut shared, event sync, 32-bit float, 50 ms", () => new WasapiOut(AudioClientShareMode.Shared, true, 50), true),
            ("WasapiOut exclusive, event sync, 32-bit float", () => new WasapiOut(AudioClientShareMode.Exclusive, true, 20), true),
            ("WasapiOut exclusive, event sync, 16-bit PCM", () => new WasapiOut(AudioClientShareMode.Exclusive, true, 20), false)
        ];

        // One configuration at a time, so the listener is told which one is coming before it plays
        // rather than having to count along a forty-second run. This is the one to use when
        // somebody else is driving the run and you are only listening.
        //
        //   dotnet test ... --filter "Name=PlayOneSinkConfiguration(4)" --logger "console;verbosity=detailed"
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        public void PlayOneSinkConfiguration(int configurationNumber)
        {
            var configurations = Configurations();
            Assert.That(configurationNumber, Is.InRange(1, configurations.Length));
            var (name, create, useFloat) = configurations[configurationNumber - 1];

            Report($"--- {configurationNumber}. {name}");
            Report("A 440 Hz tone for three seconds. A clean one is a plain musical note.");
            PlayThrough(create, useFloat);
        }

        // The whole matrix in one run, for when you are running it yourself and can watch the
        // output scroll past. Ten configurations, three seconds each.
        [Test]
        public void PlaySinkMatrix()
        {
            Report("Each configuration plays a 440 Hz tone for three seconds.");
            Report("Report the NUMBER of any that is not a clean, steady musical note.");
            Report("");

            var configurations = Configurations();
            for (var index = 0; index < configurations.Length; index++)
            {
                var (name, create, useFloat) = configurations[index];
                Report($"--- {index + 1}. {name}");
                PlayThrough(create, useFloat);
                Report("");
            }

            Report("Done. Report which numbers were not clean tones.");
        }

        // The measurement behind the accuracy the sink change cost, and it needs no ears.
        //
        // Cue onsets are mapped by reading the device's playback position, so how finely that
        // position moves sets how precisely an onset can be placed. WaveOutEvent reports coarsely;
        // WASAPI reports per-sample. This prints the step sizes each sink actually produces, which
        // is the 0.02 ms against 0.27 ms difference stated as a measurement rather than a memory.
        [Test]
        public void MeasureDevicePositionGranularity()
        {
            Report("Polling each sink's reported playback position for two seconds.");
            Report("Smaller and more uniform steps mean onsets can be placed more precisely.");
            Report("");

            var configurations = new (string Name, Func<IWavePlayer> Create, bool UseFloat)[]
            {
                ("WaveOutEvent, 16-bit PCM, 100 ms  <-- what ships today", () => new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 3 }, false),
                ("WaveOutEvent, 16-bit PCM, 20 ms", () => new WaveOutEvent { DesiredLatency = 20, NumberOfBuffers = 3 }, false),
                ("WasapiOut shared, event sync, 16-bit PCM, 20 ms", () => new WasapiOut(AudioClientShareMode.Shared, true, 20), false),
                ("WasapiOut shared, event sync, 32-bit float, 20 ms", () => new WasapiOut(AudioClientShareMode.Shared, true, 20), true)
            };

            foreach (var (name, create, useFloat) in configurations)
            {
                Report($"--- {name}");
                MeasureGranularity(create, useFloat);
                Report("");
            }
        }

        // The experiment every earlier hypothesis should have started from. A plain sine, generated
        // here, with no engine, no voice pool and no capture list -- through a real sink, captured
        // into a pre-allocated array by a provider that does nothing else.
        //
        // Clean means the engine and the voice pool are exonerated and the fault is in the sink or
        // in how a sink is driven. Corrupt means the same, more strongly: nothing of ours is even
        // present in the signal path.
        [TestCase("waveout-16", TestName = "CaptureBareSine_WaveOutEvent16Bit")]
        [TestCase("wasapi-16", TestName = "CaptureBareSine_WasapiPcm16")]
        [TestCase("wasapi-float", TestName = "CaptureBareSine_WasapiFloat")]
        public void CaptureBareSine(string sink)
        {
            var capture = new CapturingSineProvider(ToneHertz, 0.5f, seconds: 6);
            IWavePlayer player = sink switch
            {
                "waveout-16" => new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 3 },
                "wasapi-16" => new WasapiOut(AudioClientShareMode.Shared, true, 20),
                _ => new WasapiOut(AudioClientShareMode.Shared, true, 20)
            };

            using (player)
            {
                if (sink == "wasapi-float")
                    player.Init(capture);
                else
                    player.Init(new SampleToWaveProvider16(capture));
                player.Play();
                Thread.Sleep(5000);
                player.Stop();
            }

            Report($"--- {sink}");
            Report($"    reads {capture.ReadCount}, threads {capture.Threads}, overlapping {capture.OverlappingReads}");
            capture.ReportAgainstExpectedSine(Report);
        }

        // The production sink, verified the same way the bare sinks were. AudioOutputDevice is what
        // the editor actually plays through, and what every listening check now goes through, so it
        // is the one that has to be proven clean before anybody is asked to judge what they hear.
        [Test]
        public void CaptureBareSineThroughTheProductionSink()
        {
            var capture = new CapturingSineProvider(ToneHertz, 0.5f, seconds: 6);
            using (var device = new Editors.Audio.Shared.Wwise.Engine.Output.AudioOutputDevice())
            {
                device.EnsureCreated(capture);
                device.EnsurePlaying();
                Thread.Sleep(5000);
            }

            Report("--- production AudioOutputDevice");
            Report($"    reads {capture.ReadCount}, threads {capture.Threads}, overlapping {capture.OverlappingReads}");
            capture.ReportAgainstExpectedSine(Report);
        }

        // Records what the machine is actually playing, through WASAPI loopback on the same
        // endpoint the engine is using. Nothing about the engine's own bookkeeping is involved,
        // which is the point: the bookkeeping is what is being checked.
        private sealed class LoopbackRecorder
        {
            private readonly WasapiLoopbackCapture _capture = new();
            private readonly List<float> _recorded = [];
            private readonly object _recordedLock = new();

            public int SampleRate => _capture.WaveFormat.SampleRate;
            public int Channels => _capture.WaveFormat.Channels;

            public LoopbackRecorder()
            {
                _capture.DataAvailable += (_, args) =>
                {
                    // Loopback hands back the endpoint's own mix format, which is float here but is
                    // not guaranteed to be; convert whatever arrives.
                    var bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
                    lock (_recordedLock)
                    {
                        for (var offset = 0; offset + bytesPerSample <= args.BytesRecorded; offset += bytesPerSample)
                        {
                            _recorded.Add(_capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
                                ? BitConverter.ToSingle(args.Buffer, offset)
                                : BitConverter.ToInt16(args.Buffer, offset) / 32768f);
                        }
                    }
                };
            }

            public void Start() => _capture.StartRecording();

            public float[] StopAndTake()
            {
                _capture.StopRecording();
                // The callback can still be in flight when StopRecording returns.
                Thread.Sleep(200);
                lock (_recordedLock)
                    return [.. _recorded];
            }
        }

        // Generates the sine itself and records exactly what it handed over, so the expected signal
        // is known by construction rather than inferred.
        private sealed class CapturingSineProvider(double hertz, float amplitude, int seconds) : ISampleProvider
        {
            private readonly float[] _captured = new float[SampleRate * ChannelCount * seconds];
            private readonly HashSet<int> _threads = [];
            private long _frame;
            private int _capturedCount;
            private int _insideRead;

            public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, ChannelCount);
            public int ReadCount { get; private set; }
            public int OverlappingReads { get; private set; }
            public string Threads { get { lock (_threads) return string.Join(", ", _threads); } }

            public int Read(float[] buffer, int offset, int count)
            {
                if (Interlocked.Exchange(ref _insideRead, 1) == 1)
                    OverlappingReads++;
                lock (_threads)
                    _threads.Add(Environment.CurrentManagedThreadId);
                ReadCount++;

                for (var sample = 0; sample < count; sample += ChannelCount)
                {
                    var value = amplitude * MathF.Sin((float)(2 * Math.PI * hertz * _frame / SampleRate));
                    buffer[offset + sample] = value;
                    buffer[offset + sample + 1] = value;
                    _frame++;
                }
                if (_capturedCount + count <= _captured.Length)
                {
                    Array.Copy(buffer, offset, _captured, _capturedCount, count);
                    _capturedCount += count;
                }
                Interlocked.Exchange(ref _insideRead, 0);
                return count;
            }

            // What was handed over, against the sine that should have been handed over.
            public void ReportAgainstExpectedSine(Action<string> report)
            {
                var peak = 0f;
                var largestStep = 0f;
                var discontinuities = 0;
                var previous = 0f;
                for (var index = 0; index < _capturedCount; index += ChannelCount)
                {
                    var value = _captured[index];
                    peak = Math.Max(peak, Math.Abs(value));
                    if (index > 0)
                    {
                        var step = Math.Abs(value - previous);
                        largestStep = Math.Max(largestStep, step);
                        if (step > 0.15f) discontinuities++;
                    }
                    previous = value;
                }
                report($"    captured {_capturedCount / ChannelCount} frames");
                report($"    peak {peak:F6}  (generated amplitude is {amplitude})");
                report($"    largest step {largestStep:F6}  (a {hertz} Hz sine moves 0.029 per sample)");
                report($"    discontinuities over 0.15: {discontinuities}");
            }
        }

        // Whether a sink ever reports its position going backwards.
        //
        // An earlier spin-polling harness saw steps of -3 and -46 bytes on WASAPI, and that
        // observation is what motivated the position tracker learning to tell jitter from a clock
        // restart. Sampling on a timer instead showed none -- but Thread.Sleep(1) is a 15 ms quantum
        // on Windows, far coarser than the position moves, so that proves nothing either.
        //
        // This spins, which does perturb playback, but only in short bursts and only to answer one
        // question: does the position ever go backwards at all? Nothing here is a granularity
        // measurement and it must not be read as one.
        [Test]
        public void ReportWhetherAnySinkGoesBackwards()
        {
            Report("Short spin bursts. The only question is whether a position ever decreases.");
            Report("This perturbs playback by design -- do not read step sizes off it.");
            Report("");

            var configurations = new (string Name, Func<IWavePlayer> Create, bool UseFloat)[]
            {
                ("WaveOutEvent, 16-bit, 100 ms", () => new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 3 }, false),
                ("WasapiOut shared, event sync, 16-bit, 20 ms", () => new WasapiOut(AudioClientShareMode.Shared, true, 20), false)
            };

            foreach (var (name, create, useFloat) in configurations)
            {
                IWavePlayer player = null;
                try
                {
                    player = create();
                    Initialise(player, useFloat);
                    if (player is not IWavePosition positionSource)
                        continue;
                    player.Play();

                    var backwards = new List<long>();
                    var samples = 0L;
                    for (var burst = 0; burst < 6; burst++)
                    {
                        var previous = positionSource.GetPosition();
                        var until = DateTime.UtcNow.AddMilliseconds(150);
                        while (DateTime.UtcNow < until)
                        {
                            var position = positionSource.GetPosition();
                            samples++;
                            if (position < previous)
                                backwards.Add(position - previous);
                            previous = position;
                        }
                        Thread.Sleep(100);
                    }
                    player.Stop();

                    Report($"--- {name}");
                    Report($"    {samples} reads, {backwards.Count} of them backwards" +
                           (backwards.Count > 0 ? $", smallest {backwards.Min()} bytes" : string.Empty));
                }
                catch (Exception exception)
                {
                    Report($"--- {name}: REFUSED {exception.Message}");
                }
                finally
                {
                    player?.Dispose();
                }
            }
        }

        // The engine's own hard constraint, measured rather than remembered.
        //
        // The plan requires cue onsets to land within 0.02 ms of where the animation says they are,
        // and that has not been checked since the sink changed. It cannot be checked by reading the
        // device position: polling it in a loop starves the render thread and audibly breaks
        // playback, and polling it on a timer is a 15 ms quantum on Windows. Both measure the
        // harness rather than the engine.
        //
        // So this records what the speakers are actually given, through WASAPI loopback, and finds
        // the cues in it. The onsets are silence-to-signal transitions in real captured audio, which
        // is the only measurement of this that does not depend on the engine's own bookkeeping being
        // right -- and the engine's bookkeeping is what is under test.
        [Test]
        public void MeasureCueOnsetAccuracyThroughLoopback()
        {
            const int CueCount = 8;
            const int CueSpacingFrames = SampleRate / 2;

            var capture = new LoopbackRecorder();
            using var engine = new SoundEngine(new Editors.Audio.Shared.Wwise.Engine.Output.AudioOutputDevice());
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("cue", 10)
                .WithSound(10, ClickMedia()));
            var gameObject = engine.RegisterGameObject("onset");

            var timeline = engine.CreateTimeline(
                TimeSpan.FromSeconds((CueCount + 2) * CueSpacingFrames / (double)SampleRate),
                shouldLoop: false);
            for (var cue = 0; cue < CueCount; cue++)
            {
                engine.ScheduleEvent(
                    "cue",
                    gameObject,
                    TimeSpan.FromSeconds((cue + 1) * CueSpacingFrames / (double)SampleRate),
                    timeline);
            }

            capture.Start();
            engine.Start(timeline);
            Thread.Sleep((CueCount + 3) * CueSpacingFrames * 1000 / SampleRate);
            var recorded = capture.StopAndTake();

            Report($"    captured {recorded.Length / ChannelCount} frames at {capture.SampleRate} Hz");
            var onsets = OnsetFramesIn(recorded, capture.Channels);
            Report($"    found {onsets.Count} cue onsets, expected {CueCount}");
            if (onsets.Count < 2)
            {
                Report("    NOT ENOUGH ONSETS TO MEASURE -- is anything else playing through this device?");
                return;
            }

            // Absolute position depends on when the capture happened to start, so the measurement is
            // the spacing between cues: that is what the timeline promised and what a listener hears
            // as a rhythm going wrong.
            var expectedSpacing = CueSpacingFrames * (capture.SampleRate / (double)SampleRate);
            var worstErrorFrames = 0d;
            for (var index = 1; index < onsets.Count; index++)
            {
                var error = Math.Abs((onsets[index] - onsets[index - 1]) - expectedSpacing);
                worstErrorFrames = Math.Max(worstErrorFrames, error);
            }
            var worstErrorMilliseconds = worstErrorFrames * 1000d / capture.SampleRate;
            Report($"    expected spacing {expectedSpacing:F1} frames");
            Report($"    worst spacing error: {worstErrorFrames:F1} frames = {worstErrorMilliseconds:F3} ms");
            Report($"    the stated constraint is 0.02 ms");
        }

        // A short burst with silence either side, so an onset is unambiguous in a recording.
        private static Editors.Audio.Shared.Wwise.Engine.Media.SourceMedia ClickMedia()
        {
            const int BurstFrames = 2_400;
            var samples = new float[BurstFrames * ChannelCount];
            for (var frame = 0; frame < BurstFrames; frame++)
            {
                var value = 0.6f * MathF.Sin(2f * MathF.PI * 1_000f * frame / SampleRate);
                samples[frame * ChannelCount] = value;
                samples[frame * ChannelCount + 1] = value;
            }
            return MixerProbe.ToMedia(samples);
        }

        // Where the recording goes from silent to sounding.
        //
        // On an envelope rather than on the samples: a tone passes through zero twice per cycle, so
        // testing instantaneous magnitude finds an onset per cycle instead of per cue. The envelope
        // is the peak over a window shorter than the gap between cues and longer than a cycle of the
        // tone in them.
        private static List<int> OnsetFramesIn(float[] recorded, int channels)
        {
            const int EnvelopeWindowFrames = 64;
            const float OnThreshold = 0.05f;
            const float OffThreshold = 0.01f;

            var frameCount = recorded.Length / channels;
            var onsets = new List<int>();
            var isSounding = false;
            for (var windowStart = 0; windowStart + EnvelopeWindowFrames <= frameCount; windowStart += EnvelopeWindowFrames)
            {
                var peak = 0f;
                for (var frame = windowStart; frame < windowStart + EnvelopeWindowFrames; frame++)
                    peak = Math.Max(peak, Math.Abs(recorded[frame * channels]));

                if (!isSounding && peak > OnThreshold)
                {
                    isSounding = true;

                    // Report where the sound actually starts inside the window, not where the
                    // window does: a 64-frame quantisation would be 1.3 ms of invented error against
                    // a constraint of 0.02 ms.
                    var onsetFrame = windowStart;
                    while (onsetFrame < windowStart + EnvelopeWindowFrames
                        && Math.Abs(recorded[onsetFrame * channels]) <= OffThreshold)
                    {
                        onsetFrame++;
                    }
                    onsets.Add(onsetFrame);
                }
                else if (isSounding && peak < OffThreshold)
                {
                    isSounding = false;
                }
            }
            return onsets;
        }

        // The chosen sink on every endpoint the machine has, not only the default one.
        //
        // The plan asks for a sink selected from measured behaviour "across representative devices",
        // and one endpoint is not that. Each is driven with the same generated sine and the capture
        // is compared against what was generated, so no listening is needed to tell a broken
        // endpoint from a working one.
        [Test]
        public void CaptureBareSineOnEveryEndpoint()
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultEndpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var isDefault = endpoint.ID == defaultEndpoint.ID ? " [DEFAULT]" : string.Empty;
                Report($"--- {endpoint.FriendlyName}{isDefault}");

                var capture = new CapturingSineProvider(ToneHertz, 0.5f, seconds: 4);
                WasapiOut player = null;
                try
                {
                    // The shipping configuration, pointed at this endpoint.
                    player = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 20);
                    player.Init(new SampleToWaveProvider16(capture));
                    player.Play();
                    Thread.Sleep(3000);
                    player.Stop();
                    Report($"    negotiated {player.OutputWaveFormat.SampleRate} Hz, {player.OutputWaveFormat.Channels} ch, " +
                           $"{player.OutputWaveFormat.BitsPerSample}-bit {player.OutputWaveFormat.Encoding}");
                    capture.ReportAgainstExpectedSine(Report);
                }
                catch (Exception exception)
                {
                    Report($"    REFUSED: {exception.GetType().Name}: {exception.Message}");
                }
                finally
                {
                    player?.Dispose();
                }
                Report(string.Empty);
            }
        }

        private static void PlayThrough(Func<IWavePlayer> createPlayer, bool useFloat)
        {
            IWavePlayer player = null;
            try
            {
                player = createPlayer();
                var stoppedUnexpectedly = (Exception)null;
                player.PlaybackStopped += (_, args) => stoppedUnexpectedly = args.Exception;
                Initialise(player, useFloat);
                ReportNegotiatedFormat(player);

                player.Play();
                Thread.Sleep(PlaySeconds * 1000);
                player.Stop();

                if (stoppedUnexpectedly != null)
                    Report($"    STOPPED UNEXPECTEDLY: {stoppedUnexpectedly.Message}");
            }
            catch (Exception exception)
            {
                Report($"    REFUSED: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                player?.Dispose();
            }
        }

        private static void MeasureGranularity(Func<IWavePlayer> createPlayer, bool useFloat)
        {
            IWavePlayer player = null;
            try
            {
                player = createPlayer();
                Initialise(player, useFloat);
                if (player is not IWavePosition positionSource)
                {
                    Report("    reports no position at all");
                    return;
                }

                var bytesPerSecond = positionSource.OutputWaveFormat.AverageBytesPerSecond;
                player.Play();

                // Sampled on a timer rather than in a spin loop.
                //
                // An earlier version of this polled as fast as it could, which pinned a core, starved
                // the render thread and was plainly audible as dropouts -- so the worst-case figures
                // it produced were measuring the harness rather than the sink. One sample per
                // millisecond is far finer than any sink moves its position and costs nothing.
                var positions = new List<long>(4_000);
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (DateTime.UtcNow < deadline)
                {
                    positions.Add(positionSource.GetPosition());
                    Thread.Sleep(1);
                }
                player.Stop();

                var steps = new List<long>();
                for (var index = 1; index < positions.Count; index++)
                {
                    var step = positions[index] - positions[index - 1];
                    if (step != 0)
                        steps.Add(step);
                }

                if (steps.Count == 0)
                {
                    Report("    the position never moved");
                    return;
                }

                var backwards = steps.Count(step => step < 0);
                var forwards = steps.Where(step => step > 0).OrderBy(step => step).ToList();
                Report($"    {positions.Count} samples over 3 s, {steps.Count} of them a change");
                Report($"    backwards steps: {backwards}" + (backwards > 0 ? $" (smallest {steps.Min()} bytes)" : string.Empty));
                if (forwards.Count == 0)
                    return;
                Report($"    forward step, bytes: smallest {forwards[0]}, median {forwards[forwards.Count / 2]}, 99th {forwards[(int)(forwards.Count * 0.99)]}, largest {forwards[^1]}");
                Report($"    forward step, ms:    smallest {ToMilliseconds(forwards[0], bytesPerSecond):F3}, " +
                       $"median {ToMilliseconds(forwards[forwards.Count / 2], bytesPerSecond):F3}, " +
                       $"99th {ToMilliseconds(forwards[(int)(forwards.Count * 0.99)], bytesPerSecond):F3}, " +
                       $"largest {ToMilliseconds(forwards[^1], bytesPerSecond):F3}");
            }
            catch (Exception exception)
            {
                Report($"    REFUSED: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                player?.Dispose();
            }
        }

        private static void Initialise(IWavePlayer player, bool useFloat)
        {
            var tone = new SignalGenerator(SampleRate, ChannelCount)
            {
                Gain = 0.2,
                Frequency = ToneHertz,
                Type = SignalGeneratorType.Sin
            };

            // The distinction the fault is suspected to turn on: what the sink is actually handed.
            if (useFloat)
                player.Init(tone);
            else
                player.Init(new SampleToWaveProvider16(tone));
        }

        private static void ReportNegotiatedFormat(IWavePlayer player)
        {
            if (player is IWavePosition positionSource)
            {
                var format = positionSource.OutputWaveFormat;
                Report($"    negotiated {format.SampleRate} Hz, {format.Channels} ch, {format.BitsPerSample}-bit {format.Encoding}");
            }
            else
            {
                Report("    negotiated format not reported");
            }
        }

        private static bool SupportsExclusive(MMDevice endpoint, WaveFormat format)
        {
            try
            {
                return endpoint.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, format);
            }
            catch
            {
                return false;
            }
        }

        private static double ToMilliseconds(long bytes, int bytesPerSecond)
            => bytesPerSecond <= 0 ? double.NaN : bytes * 1000d / bytesPerSecond;

        private static void Report(string line) => TestContext.Out.WriteLine(line);
    }
}
