using System.Collections.Concurrent;
using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Commands;
using Editors.Audio.Shared.Wwise.Engine.Rendering;

namespace Test.Audio
{
    // The control side and the render thread share the renderer's state, and until now that had
    // only been reasoned about. This drives both at once so the reasoning has something to fail
    // against, and the voice layer gets considerably more stateful from here on.
    //
    // "The control side" is deliberately not "the control thread": the game layer calls in on the
    // UI thread while the completion poll applies event-authored switch and state changes, and
    // forgets finished posts, on a timer thread. Both walk the engine's post lists and its game
    // object registry, so the last test here drives that pair as well.
    public class SoundEngineConcurrencyTests
    {
        private static readonly TransportId Transport = new(1);
        private const long TimelineDurationFrames = 4_800;
        private const int MaximumChunkFrames = 512;
        private static readonly TimeSpan ThreadJoinTimeout = TimeSpan.FromSeconds(30);

        [Test]
        public void TheMixer_WithstandsCommandsArrivingWhileItRenders()
        {
            const int RenderIterations = 4_000;

            // The control thread posts in bursts and then waits, because that is what a UI thread
            // does. Before phase 1 bounded the drain it had to: a producer that outran
            // ApplyPendingCommands held Read inside the drain loop for as long as it kept posting,
            // and this test deadlocked. The bound itself is guarded by
            // TheCommandDrain_IsBoundedPerRender; the burst here is realism, not a workaround.
            const int CommandsPerBurst = 8;

            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport, TimelineDurationFrames, true));
            renderer.SubmitCommand(EngineCommand.Start(Transport, 0));

            var failures = new ConcurrentQueue<Exception>();
            var isRenderingComplete = false;
            var renderedFrameCount = 0L;

            var renderThread = StartThread("render", failures, () =>
            {
                var buffer = new float[MaximumChunkFrames * PlaybackFormat.ChannelCount];
                var chunkSizes = new Random(17);
                for (var iteration = 0; iteration < RenderIterations; iteration++)
                {
                    var chunkFrames = chunkSizes.Next(1, MaximumChunkFrames + 1);
                    renderer.Render(buffer, 0, chunkFrames * PlaybackFormat.ChannelCount);
                    renderedFrameCount += chunkFrames;
                }
                Volatile.Write(ref isRenderingComplete, true);
            });

            var controlThread = StartThread("control", failures, () =>
            {
                var commandChoices = new Random(29);
                var cue = MixerProbe.ConstantPcm(0.25f, 480);
                var nextPlayingIdentifier = 0L;
                while (!Volatile.Read(ref isRenderingComplete))
                {
                    for (var command = 0; command < CommandsPerBurst; command++)
                        renderer.SubmitCommand(commandChoices.Next(6) switch
                        {
                            0 => EngineCommand.ScheduleEvent(new PlayingId(++nextPlayingIdentifier, Transport), default, MixerProbe.SingleSoundEvent(cue), commandChoices.Next((int)TimelineDurationFrames)),
                            1 => EngineCommand.SeekTimeline(Transport, commandChoices.Next((int)TimelineDurationFrames)),
                            2 => EngineCommand.PauseTimeline(Transport),
                            3 => EngineCommand.ResumeTimeline(Transport, 0),
                            4 => EngineCommand.StopPlayingId(new PlayingId(commandChoices.Next(1, (int)Math.Max(2, nextPlayingIdentifier)), Transport)),
                            _ => EngineCommand.Start(Transport, commandChoices.Next((int)TimelineDurationFrames))
                        });
                    Thread.Sleep(1);
                }
            });

            // Standing in for the animation player, which reads the position and the ledger from
            // a third thread while both of the others are running.
            var observerThread = StartThread("observer", failures, () =>
            {
                while (!Volatile.Read(ref isRenderingComplete))
                {
                    var timelinePosition = renderer.PlaybackPositionFrames;
                    if (timelinePosition < 0 || timelinePosition > TimelineDurationFrames)
                        failures.Enqueue(new InvalidOperationException(
                            $"the timeline position was {timelinePosition}, outside [0, {TimelineDurationFrames}]"));

                    var lastRenderedOutputFrame = renderer.RenderedOutputFrameCount - 1;
                    if (lastRenderedOutputFrame >= 0
                        && renderer.TryGetTimelineFrame(lastRenderedOutputFrame, Transport, out var mappedTimelineFrame)
                        && (mappedTimelineFrame < 0 || mappedTimelineFrame > TimelineDurationFrames))
                        failures.Enqueue(new InvalidOperationException(
                            $"output frame {lastRenderedOutputFrame} mapped to timeline frame {mappedTimelineFrame}"));

                }
            });

            JoinOrFail(renderThread, controlThread, observerThread);

            Assert.That(failures, Is.Empty, () => string.Join(Environment.NewLine, failures.Select(failure => failure.ToString())));
            Assert.That(renderer.RenderedOutputFrameCount, Is.EqualTo(renderedFrameCount), "every frame the device asked for was accounted for");
        }

        // A render callback that keeps draining for as long as the control thread keeps posting
        // never returns, and a late buffer is not recoverable. This is the bound that stops it —
        // before phase 1 put it in, the stress test above deadlocked.
        [Test]
        public void TheCommandDrain_IsBoundedPerRender()
        {
            const int CommandCount = 10_000;

            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport, TimelineDurationFrames, true));
            var lastSubmittedCommandSequence = 0L;
            for (var commandIndex = 0; commandIndex < CommandCount; commandIndex++)
                lastSubmittedCommandSequence = renderer.SubmitCommand(EngineCommand.SeekTimeline(Transport, commandIndex % TimelineDurationFrames));

            var buffer = new float[PlaybackFormat.ChannelCount];
            renderer.Render(buffer, 0, buffer.Length);

            Assert.That(
                renderer.LastAppliedCommandSequence,
                Is.LessThan(lastSubmittedCommandSequence),
                "one render applied the whole backlog instead of leaving it for the next block");

            // The backlog is not dropped, only deferred.
            renderer.ProcessPendingCommandsWithoutRendering();
            Assert.That(renderer.LastAppliedCommandSequence, Is.EqualTo(lastSubmittedCommandSequence));
        }

        // The engine decides a position command has taken effect by comparing the renderer's last
        // applied sequence against the one it is waiting on, which only holds if commands are
        // applied in the order one control thread submitted them.
        [Test]
        public void EveryCommandFromTheControlThread_IsAppliedInTheOrderItWasSubmitted()
        {
            const int CommandCount = 20_000;
            const int CommandsPerBurst = 8;

            var renderer = new AudioRenderer();
            renderer.SubmitCommand(EngineCommand.CreateTimeline(Transport, TimelineDurationFrames, true));

            var failures = new ConcurrentQueue<Exception>();
            var isSubmissionComplete = false;
            var lastSubmittedCommandSequence = 0L;

            var renderThread = StartThread("render", failures, () =>
            {
                var buffer = new float[MaximumChunkFrames * PlaybackFormat.ChannelCount];
                while (!Volatile.Read(ref isSubmissionComplete))
                    renderer.Render(buffer, 0, buffer.Length);
            });

            var controlThread = StartThread("control", failures, () =>
            {
                for (var commandIndex = 0; commandIndex < CommandCount; commandIndex++)
                {
                    lastSubmittedCommandSequence = renderer.SubmitCommand(EngineCommand.SeekTimeline(Transport, commandIndex % TimelineDurationFrames));
                    if (commandIndex % CommandsPerBurst == CommandsPerBurst - 1)
                        Thread.Sleep(0);
                }
                Volatile.Write(ref isSubmissionComplete, true);
            });

            JoinOrFail(controlThread, renderThread);
            renderer.ProcessPendingCommandsWithoutRendering();

            Assert.That(failures, Is.Empty, () => string.Join(Environment.NewLine, failures.Select(failure => failure.ToString())));
            Assert.That(renderer.LastAppliedCommandSequence, Is.EqualTo(lastSubmittedCommandSequence));
        }

        // The engine's post lists and its game object registry are plain collections, and two
        // control-side threads reach them: the game layer on the UI thread, and the completion
        // poll's timer thread re-warming what an event's own SetSwitch or SetState action moved.
        //
        // Before _controlStateLock this threw — a List<T> mutated during another thread's ToArray,
        // and a Dictionary grown under a live enumeration. Driven through the public surface,
        // because the two paths that collide are both reachable from it.
        [Test]
        public void TheEngine_WithstandsGameLayerCallsArrivingWhileTheCompletionPollRewarms()
        {
            const int Iterations = 2_000;

            var media = MixerProbe.ConstantPcm(0.25f, 480);
            var outputDevice = new FakeAudioOutputDevice();
            using var engine = new SoundEngine(outputDevice);
            engine.LoadHierarchy(new FakeHierarchy()
                .WithEvent("cue", 10)
                .WithSwitchContainer(10, "mode", "idle", ("idle", new uint[] { 1 }), ("drive", new uint[] { 2 }))
                .WithSound(1, media)
                .WithSound(2, media));

            var failures = new ConcurrentQueue<Exception>();
            var isGameLayerComplete = false;

            // Registering, scheduling and stopping: everything that adds to or removes from the
            // lists the re-warm walks.
            var gameLayerThread = StartThread("game layer", failures, () =>
            {
                var timeline = engine.CreateTimeline(TimeSpan.FromSeconds(1), shouldLoop: true);
                for (var iteration = 0; iteration < Iterations; iteration++)
                {
                    var gameObject = engine.RegisterGameObject($"emitter-{iteration}");
                    var scheduled = engine.ScheduleEvent("cue", gameObject, TimeSpan.Zero, timeline);
                    engine.PostEvent("cue", gameObject);
                    engine.GetPlayableSourceIds("cue", gameObject);
                    engine.StopPlayingId(scheduled.PlayingId);
                    engine.UnregisterGameObject(gameObject);
                }
                Volatile.Write(ref isGameLayerComplete, true);
            });

            // Standing in for the completion poll: the same re-warm, over every game object and
            // every post, from a thread the game layer knows nothing about.
            var pollThread = StartThread("completion poll", failures, () =>
            {
                var values = new Random(53);
                while (!Volatile.Read(ref isGameLayerComplete))
                    engine.SetState("mode", values.Next(2) == 0 ? "idle" : "drive");
            });

            JoinOrFail(gameLayerThread, pollThread);

            Assert.That(failures, Is.Empty, () => string.Join(Environment.NewLine, failures.Select(failure => failure.ToString())));
        }

        private static Thread StartThread(string threadName, ConcurrentQueue<Exception> failures, Action work)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    work();
                }
                catch (Exception failure)
                {
                    failures.Enqueue(failure);
                }
            })
            {
                Name = threadName,
                IsBackground = true
            };
            thread.Start();
            return thread;
        }

        private static void JoinOrFail(params Thread[] threads)
        {
            foreach (var thread in threads)
            {
                if (!thread.Join(ThreadJoinTimeout))
                    Assert.Fail($"the {thread.Name} thread did not finish within {ThreadJoinTimeout}");
            }
        }
    }
}
