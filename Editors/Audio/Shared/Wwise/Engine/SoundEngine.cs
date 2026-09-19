using System.Collections.Concurrent;
using Editors.Audio.Shared.Wwise.Engine.Behaviour;
using Editors.Audio.Shared.Wwise.Engine.Commands;
using Editors.Audio.Shared.Wwise.Engine.Hierarchy;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Output;
using Editors.Audio.Shared.Wwise.Engine.Rendering;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Editors.Audio.Shared.Wwise.Engine.Timing;
using Shared.GameFormats.Wwise.Enums;
using System.Numerics;

namespace Editors.Audio.Shared.Wwise.Engine
{
    // A post waiting on the transport. The playing id is what asks after it; the duration is the
    // longest sound it could pick, because which one it will pick is not settled until the cue
    // fires and the container chooses.
    public readonly record struct ScheduledPost(PlayingId PlayingId, TimeSpan LongestCandidateDuration);

    public interface ISoundEngine : IDisposable
    {
        // Compatibility view of the current scheduled timeline. New consumers should retain the
        // identity they created or posted and use the scoped queries and controls below.
        SoundPlaybackState PlaybackState { get; }
        TimeSpan Position { get; }
        TransportId TransportId { get; }
        SoundPlaybackState? GetPlaybackState(TransportId transportId);
        SoundPlaybackState? GetPlaybackState(PlayingId playingId);
        TimeSpan? GetPosition(TransportId transportId);

        // The banks the engine resolves events against. Handed in rather than injected because the
        // engine outlives any one editor, and whichever editor is driving it supplies the hierarchy
        // it wants played — the same reason the transport is identified at all.
        void LoadHierarchy(IHierarchyProvider hierarchyProvider);

        GameObjectId RegisterGameObject(string name);
        void UnregisterGameObject(GameObjectId gameObject);
        void SetSwitch(string switchGroupName, string switchValueName, GameObjectId gameObject);

        // A state is the game's rather than an emitter's — Wwise scopes a state group globally —
        // so this takes no game object, while a game parameter, which is per emitter, does.
        void SetState(string stateGroupName, string stateValueName);
        void SetGameParameter(string gameParameterName, float value, GameObjectId gameObject);
        void SetPosition(GameObjectId gameObject, Vector3 position);
        void SetListeners(GameObjectId gameObject, params GameObjectId[] listeners);

        // Every sound an event could reach on this game object as its switches currently stand.
        // A container has no one answer until it picks, so this is the whole candidate set rather
        // than the file that will be heard.
        IReadOnlyList<uint> GetPlayableSourceIds(string eventName, GameObjectId gameObject);

        // One entry point per level of the hierarchy the caller is actually holding, rather than
        // every tool having to synthesise an event id it does not have. They converge one level
        // down: PlayNode runs the same walker as PostEvent, and PlayMedia still goes through the
        // voice chain, the bus and the limiter, so a WEM audition and a Super View cue sound the
        // same. All three return a PlayingId, so stopping and position readback never depend on
        // which way in was used.
        PlayingId PostEvent(string eventName, GameObjectId gameObject);
        PlayingId PostEvent(uint eventId, GameObjectId gameObject);

        // A playable node with no event above it — a container or a sound. An extension
        // rather than replicated behaviour: the Wwise runtime API has no verb for it, but the
        // Wwise authoring transport plays these nodes directly, and the audio explorer is an
        // authoring tool. Actor Mixers are deliberately excluded: Wwise marks them non-playable.
        PlayingId PlayNode(uint nodeId, GameObjectId gameObject);

        ScheduledPost ScheduleEvent(string eventName, GameObjectId gameObject, TimeSpan timelinePosition, TransportId transportId);
        void StopPlayingId(PlayingId playingId);

        TransportId CreateTimeline(TimeSpan timelineDuration, bool shouldLoop);
        void Start(TransportId transportId, TimeSpan? playbackPosition = null);
        void Pause(TransportId transportId);
        void Resume(TransportId transportId);
        void Stop(TransportId transportId);
        void Seek(TransportId transportId, TimeSpan playbackPosition);
        void Pause();
        void Resume();
        void Stop();

        // Decoded audio and nothing else, for the waveform visualiser and the WAV audition that
        // runs through it. It plays on a reserved game object, so no tool has to register one to
        // hear a file and the direct path is not a hole in the parameter pipeline.
        PlayingId PlayMedia(SourceMedia media, bool shouldLoop = false, TimeSpan? startPosition = null);

        void Pause(PlayingId playingId);
        void Resume(PlayingId playingId);
        void Seek(PlayingId playingId, TimeSpan playbackPosition);
        TimeSpan? GetPosition(PlayingId playingId);
        event Action<PlayingId> PostCompleted;
        event Action<TransportId> PlaybackCompleted;
    }

    public sealed class SoundEngine : ISoundEngine
    {
        // Frequent enough that playback completion is reported without an audible gap, but
        // far cheaper than polling every output callback.
        private const int PlaybackCompletionPollIntervalMilliseconds = 25;

        // A scheduled post as the control side remembers it, so that a switch change can send the
        // walk round again rather than leaving the timeline holding a branch the game object has
        // since left.
        private sealed record WarmedPost(PlayingId PlayingId, string EventName, GameObjectId GameObject, TimeSpan TimelinePosition);
        private sealed record DynamicPost(
            PlayingId PlayingId,
            GameObjectId GameObject,
            Func<EventProcessor, GameObjectState, ResolvedEvent> Warm);

        private readonly ILogger _logger = Logging.Create<SoundEngine>();
        private readonly AudioRenderer _audioRenderer;
        private readonly AudioRendererSampleProvider _audioRendererSampleProvider;
        private readonly IAudioOutputDevice _audioOutputDevice;
        private readonly PlaybackPositionTracker _playbackPositionTracker;
        private readonly Timer _playbackCompletionTimer;

        // The control side is not one thread, and calling it "the control thread" hid that. The
        // game layer calls in on the UI thread while the completion poll runs on a timer thread,
        // and phase 11 gave that poll real control-side work: applying what a SetSwitch or SetState
        // action changed, and forgetting a post that has finished. Both walk the same registry and
        // the same post lists the UI thread is mutating, so the control side takes a lock.
        //
        // The audio thread never takes it. It reads the command FIFO and the published parameter
        // snapshot and nothing else, so no render callback can ever be held here — which is the
        // property that makes this lock safe to hold across a hierarchy walk.
        private readonly object _controlStateLock = new();

        // The upper engine. All of it is control side: warming walks the hierarchy, reads switch
        // values and decodes media, none of which the audio thread may do.
        private readonly GameObjectRegistry _gameObjects = new();

        // Who direct media plays on. Wwise has no emitter for a file, but everything downstream
        // is keyed by game object, so giving the direct path one keeps it inside the same
        // machinery rather than beside it.
        private readonly GameObjectId _directMediaGameObject;
        private readonly List<WarmedPost> _warmedPosts = [];
        private readonly List<DynamicPost> _dynamicPosts = [];
        private readonly ConcurrentDictionary<PlayingId, PostCompletionState> _postCompletions = new();
        private readonly ConcurrentDictionary<PlayingId, SoundPlaybackState> _immediatePlaybackStates = new();
        private EventProcessor _eventProcessor;

        private long _nextPlayingIdentifier;
        private long _nextTransportIdentifier;
        private long _currentTimelineIdentifier;
        private long _lastReportedCompletedTransportIdentifier;
        private long _lastReportedUnstartedVoiceCount;
        private long _lastReportedStolenVoiceCount;
        private long _lastReportedUnscheduledEventCount;
        private long _lastReportedSilentCueCount;
        private long _lastReportedBlockOverrunCount;
        private int _lastReportedPeakSoundingVoiceCount;
        private float _lastReportedMasterPeakLevel;
        private long _lastReportedUnsupportedEffectCount;
        private long _lastReportedUnsupportedAuxiliarySendCount;
        private int _lastReportedPeakVirtualVoiceCount;
        private long _lastReportedOverLimitVirtualisedVoiceCount;
        private long _lastReportedThresholdVirtualisedVoiceCount;
        private long _lastReportedThresholdKilledVoiceCount;
        private float _lastReportedHighestBusPeakLevel;
        private int _requestedPlaybackState = (int)SoundPlaybackState.Stopped;
        private int _isCompletionPollActive;
        private volatile bool _isDisposed;

        internal SoundEngine(
            IAudioOutputDevice audioOutputDevice)
        {
            _audioRenderer = new AudioRenderer();
            _audioRendererSampleProvider = new AudioRendererSampleProvider(_audioRenderer);
            _audioOutputDevice = audioOutputDevice;
            _playbackPositionTracker = new PlaybackPositionTracker(_audioRenderer, _audioOutputDevice);
            _playbackCompletionTimer = new Timer(PollPlaybackCompletion, null, Timeout.Infinite, Timeout.Infinite);
            _directMediaGameObject = _gameObjects.Register("direct media");
        }

        public TimeSpan Position => PlaybackTime.FromFrames(_playbackPositionTracker.GetAudiblePositionFrames(PlaybackState, TransportId, _playbackPositionTracker.CaptureAbsoluteOutputFrame()));
        public TransportId TransportId => new(Interlocked.Read(ref _currentTimelineIdentifier));
        public SoundPlaybackState PlaybackState => (SoundPlaybackState)Volatile.Read(ref _requestedPlaybackState);
        public SoundPlaybackState? GetPlaybackState(TransportId transportId)
            => transportId != default && transportId == TransportId ? PlaybackState : null;
        public SoundPlaybackState? GetPlaybackState(PlayingId playingId)
            => _immediatePlaybackStates.TryGetValue(playingId, out var playbackState) ? playbackState : null;
        public TimeSpan? GetPosition(TransportId transportId)
            => GetPlaybackState(transportId).HasValue ? Position : null;
        // What the engine did that nobody asked it to do. Reported as deltas by the completion
        // poll below, and readable directly by anything that would rather show them than read a log.
        internal EngineTelemetry Telemetry => _audioRenderer.Telemetry;
        public event Action<PlayingId> PostCompleted;
        public event Action<TransportId> PlaybackCompleted;

        public void LoadHierarchy(IHierarchyProvider hierarchyProvider)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(hierarchyProvider);
            lock (_controlStateLock)
                _eventProcessor = new EventProcessor(hierarchyProvider, new NodeWalker(hierarchyProvider), Telemetry);
            _audioRenderer.ConfigureBuses(hierarchyProvider.GetMasterMixerNodes());
        }

        public GameObjectId RegisterGameObject(string name)
        {
            ThrowIfDisposed();
            lock (_controlStateLock)
                return _gameObjects.Register(name);
        }

        public void UnregisterGameObject(GameObjectId gameObject)
        {
            if (_isDisposed)
                return;
            lock (_controlStateLock)
            {
                _gameObjects.Unregister(gameObject);
                _warmedPosts.RemoveAll(warmedPost => warmedPost.GameObject == gameObject);
                _dynamicPosts.RemoveAll(post => post.GameObject == gameObject);
            }
        }

        // Re-warms everything already scheduled on this game object, because a plan warmed against
        // the old value describes a branch the game object is no longer in. Decoding here is free:
        // switch changes arrive on the control side, never on the audio thread.
        public void SetSwitch(string switchGroupName, string switchValueName, GameObjectId gameObject)
        {
            ThrowIfDisposed();
            lock (_controlStateLock)
            {
                var gameObjectState = _gameObjects.Find(gameObject);
                if (gameObjectState == null || !gameObjectState.SetSwitch(switchGroupName, switchValueName))
                    return;

                RewarmScheduledPosts(gameObject);
                RewarmDynamicPosts(gameObject);
            }
        }

        // Callers hold _controlStateLock: this reads the post list and the registry and warms
        // against both, so it cannot run beside a game layer call that is changing either.
        private void RewarmDynamicPosts(GameObjectId? gameObject)
        {
            foreach (var post in _dynamicPosts.ToArray())
            {
                if (gameObject.HasValue && post.GameObject != gameObject.Value)
                    continue;
                if (!_immediatePlaybackStates.ContainsKey(post.PlayingId))
                    continue;

                var gameObjectState = _gameObjects.Find(post.GameObject);
                if (gameObjectState == null)
                    continue;
                var resolvedEvent = post.Warm(_eventProcessor, gameObjectState);
                if (resolvedEvent == null || !resolvedEvent.RequiresContinuousValidation)
                    continue;

                _audioRenderer.SubmitCommand(EngineCommand.RetargetPlayingId(
                    post.PlayingId,
                    post.GameObject,
                    resolvedEvent,
                    CompletionFor(post.PlayingId)));
            }
        }

        // Everything scheduled against a value that has moved, walked again and put back. A state
        // change is not scoped to an emitter, so a null game object means every post rather than
        // none. Callers hold _controlStateLock, for the same reason RewarmDynamicPosts does.
        private void RewarmScheduledPosts(GameObjectId? gameObject)
        {
            foreach (var warmedPost in _warmedPosts.ToArray())
            {
                if (gameObject.HasValue && warmedPost.GameObject != gameObject.Value)
                    continue;
                if (warmedPost.PlayingId.TransportId != TransportId)
                    continue;

                var gameObjectState = _gameObjects.Find(warmedPost.GameObject);
                if (gameObjectState == null)
                    continue;

                var resolvedEvent = Warm(warmedPost.EventName, gameObjectState);
                if (resolvedEvent == null)
                    continue;

                // Taken out and put back rather than edited in place, so the renderer only ever
                // sees a whole plan and the FIFO keeps the two in order.
                _audioRenderer.SubmitCommand(EngineCommand.StopPlayingId(warmedPost.PlayingId));
                _audioRenderer.SubmitCommand(EngineCommand.ScheduleEvent(
                    warmedPost.PlayingId,
                    warmedPost.GameObject,
                    resolvedEvent,
                    PlaybackTime.ToFrames(warmedPost.TimelinePosition),
                    CompletionFor(warmedPost.PlayingId)));
            }
        }

        // What an event's SetSwitch and SetState actions changed, applied here because the registry
        // they move and the plans they invalidate are control-side data. The render callback
        // published them; this is the first moment anything is allowed to act on them.
        //
        // This runs on the completion poll's timer thread, which is why the lock exists: the game
        // layer can be registering a game object or scheduling a post on the UI thread at the same
        // instant, over the very collections this walks.
        private void ApplyPendingTransitions()
        {
            while (_audioRenderer.TryTakePendingTransition(out var transition))
            {
                if (transition.GroupType == AkGroupType.State)
                {
                    SetState(transition.GroupName, transition.ValueName);
                    continue;
                }

                lock (_controlStateLock)
                {
                    // Materialised because a re-warm can post, and posting registers nothing here
                    // but leaves the registry free to grow under a live enumeration.
                    foreach (var gameObjectState in _gameObjects.All.ToArray())
                    {
                        if (gameObjectState.SetSwitch(transition.GroupName, transition.ValueName))
                        {
                            RewarmScheduledPosts(gameObjectState.Id);
                            RewarmDynamicPosts(gameObjectState.Id);
                        }
                    }
                }
            }
        }

        // Re-warms everything scheduled against the state that moved, for the same reason a switch
        // change does: a plan warmed against the old value describes a branch the game has left.
        public void SetState(string stateGroupName, string stateValueName)
        {
            ThrowIfDisposed();
            lock (_controlStateLock)
            {
                if (!_gameObjects.SetState(stateGroupName, stateValueName))
                    return;

                RewarmScheduledPosts(gameObject: null);
                RewarmDynamicPosts(gameObject: null);
            }
        }

        // Stored and published for the parameter pipeline to read. Layer-container RTPC curves use
        // it now; broader RTPC-driven voice properties remain later work.
        public void SetGameParameter(string gameParameterName, float value, GameObjectId gameObject)
        {
            ThrowIfDisposed();
            lock (_controlStateLock)
            {
                var gameObjectState = _gameObjects.Find(gameObject);
                if (gameObjectState?.SetGameParameter(gameParameterName, value) == true)
                    RewarmDynamicPosts(gameObject);
            }
        }

        public void SetPosition(GameObjectId gameObject, Vector3 position)
        {
            ThrowIfDisposed();
            lock (_controlStateLock)
            {
                if (!_gameObjects.SetPosition(gameObject, position))
                    return;
                RewarmScheduledPosts(gameObject);
                RewarmDynamicPosts(gameObject);
            }
        }

        public void SetListeners(GameObjectId gameObject, params GameObjectId[] listeners)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(listeners);
            lock (_controlStateLock)
            {
                if (!_gameObjects.SetListeners(gameObject, listeners))
                    return;
                RewarmScheduledPosts(gameObject);
                RewarmDynamicPosts(gameObject);
            }
        }

        public IReadOnlyList<uint> GetPlayableSourceIds(string eventName, GameObjectId gameObject)
        {
            ThrowIfDisposed();
            lock (_controlStateLock)
            {
                var gameObjectState = _gameObjects.Find(gameObject);
                if (gameObjectState == null)
                    return [];
                return Warm(eventName, gameObjectState)?.PlayableSourceIds ?? [];
            }
        }

        public PlayingId PostEvent(string eventName, GameObjectId gameObject)
            => Post(gameObject, (eventProcessor, gameObjectState) => eventProcessor.Warm(eventName, gameObjectState));

        public PlayingId PostEvent(uint eventId, GameObjectId gameObject)
            => Post(gameObject, (eventProcessor, gameObjectState) => eventProcessor.Warm(eventId, gameObjectState));

        public PlayingId PlayNode(uint nodeId, GameObjectId gameObject)
            => Post(gameObject, (eventProcessor, gameObjectState) => eventProcessor.WarmNode(nodeId, gameObjectState));

        // Warmed here, on the control side, before the command is queued. Being immediate is
        // not a licence to decode on the audio thread: a post from a UI click takes its media
        // references exactly as a scheduled cue does at schedule time.
        private PlayingId Post(GameObjectId gameObject, Func<EventProcessor, GameObjectState, ResolvedEvent> warm)
        {
            ThrowIfDisposed();
            PlayingId playingId;
            lock (_controlStateLock)
            {
                if (_eventProcessor == null)
                {
                    _logger.Here().Warning("Nothing can be posted because no sound banks have been loaded into the engine");
                    return default;
                }

                var gameObjectState = _gameObjects.Find(gameObject);
                if (gameObjectState == null)
                    return default;

                // An event with no sound of its own is still worth posting: one whose actions only
                // stop or pause what is already sounding has nothing to warm and everything to do.
                var resolvedEvent = warm(_eventProcessor, gameObjectState);
                if (resolvedEvent == null || (resolvedEvent.PlayableSoundCount == 0 && resolvedEvent.Actions.Length == 0))
                    return default;

                EnsureOutputDeviceCreated();
                var transportId = NextTransportId();
                playingId = NextPlayingId(transportId);
                var postCompletion = TrackCompletion(playingId);
                _immediatePlaybackStates[playingId] = SoundPlaybackState.Playing;

                _audioRenderer.SubmitCommand(EngineCommand.PostEvent(playingId, gameObject, resolvedEvent, postCompletion));
                if (resolvedEvent.RequiresContinuousValidation)
                    _dynamicPosts.Add(new DynamicPost(playingId, gameObject, warm));
            }

            StartOutputAndAnchorPosition();
            SchedulePlaybackCompletionPoll();
            return playingId;
        }

        // Scheduling no longer applies the command itself. The render thread drains a bounded
        // number of commands per block, but a timeline does not advance until its Start is applied,
        // and Start is queued behind every ScheduleEvent that preceded it — so a whole animation's
        // cues are always in place before the first frame of it is rendered, however long the
        // backlog.
        public ScheduledPost ScheduleEvent(string eventName, GameObjectId gameObject, TimeSpan timelinePosition, TransportId transportId)
        {
            ThrowIfDisposed();
            if (transportId != TransportId)
                return default;

            lock (_controlStateLock)
            {
                var gameObjectState = _gameObjects.Find(gameObject);
                if (gameObjectState == null)
                    return default;

                var resolvedEvent = Warm(eventName, gameObjectState);
                if (resolvedEvent == null || resolvedEvent.PlayableSoundCount == 0)
                    return default;

                var playingId = NextPlayingId(transportId);
                var postCompletion = TrackCompletion(playingId);
                _warmedPosts.Add(new WarmedPost(playingId, eventName, gameObject, timelinePosition));
                _audioRenderer.SubmitCommand(EngineCommand.ScheduleEvent(
                    playingId,
                    gameObject,
                    resolvedEvent,
                    PlaybackTime.ToFrames(timelinePosition),
                    postCompletion));

                return new ScheduledPost(playingId, resolvedEvent.LongestCandidateDuration);
            }
        }

        public PlayingId PlayMedia(SourceMedia media, bool shouldLoop = false, TimeSpan? startPosition = null)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(media);
            EnsureOutputDeviceCreated();
            var transportId = NextTransportId();
            var playingId = NextPlayingId(transportId);
            var postCompletion = TrackCompletion(playingId);
            var startPositionFrame = PlaybackTime.ToFrames(startPosition ?? TimeSpan.Zero);
            _immediatePlaybackStates[playingId] = SoundPlaybackState.Playing;

            _audioRenderer.SubmitCommand(EngineCommand.PlayMedia(
                playingId,
                _directMediaGameObject,
                media,
                startPositionFrame,
                shouldLoop,
                postCompletion));
            StartOutputAndAnchorPosition();
            SchedulePlaybackCompletionPoll();
            return playingId;
        }

        public TransportId CreateTimeline(TimeSpan timelineDuration, bool shouldLoop)
        {
            ThrowIfDisposed();
            var replacedTransportId = TransportId;
            RemovePostsOwnedBy(replacedTransportId);
            var transportId = NextTransportId();
            Interlocked.Exchange(ref _currentTimelineIdentifier, transportId.Value);
            lock (_controlStateLock)
                _warmedPosts.Clear();
            var commandSequence = _audioRenderer.SubmitCommand(EngineCommand.CreateTimeline(
                transportId,
                PlaybackTime.ToFrames(timelineDuration),
                shouldLoop));
            _playbackPositionTracker.SetPendingPosition(0, commandSequence);
            Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Stopped);
            return transportId;
        }

        public void Start(TransportId transportId, TimeSpan? playbackPosition = null)
        {
            ThrowIfDisposed();
            if (transportId != TransportId)
                return;
            EnsureOutputDeviceCreated();
            Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Playing);
            var playbackPositionFrame = PlaybackTime.ToFrames(playbackPosition ?? TimeSpan.Zero);
            var commandSequence = _audioRenderer.SubmitCommand(EngineCommand.Start(transportId, playbackPositionFrame));
            _playbackPositionTracker.SetPendingPosition(playbackPositionFrame, commandSequence);
            StartOutputAndAnchorPosition();
            SchedulePlaybackCompletionPoll();
        }

        // The transport ramps itself to silence and freezes where it lands, so there is nothing to
        // seek back to and no reason to tear the output client down. Dropping that stop is what
        // removes the ~50 ms block this used to put on the UI thread.
        public void Pause()
            => Pause(TransportId);

        public void Pause(TransportId transportId)
        {
            if (_isDisposed || transportId != TransportId || PlaybackState != SoundPlaybackState.Playing)
                return;
            Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Paused);
            _audioRenderer.SubmitCommand(EngineCommand.PauseTimeline(transportId));
        }

        public void Resume()
            => Resume(TransportId);

        public void Resume(TransportId transportId)
        {
            ThrowIfDisposed();
            if (transportId != TransportId || PlaybackState != SoundPlaybackState.Paused)
                return;
            EnsureOutputDeviceCreated();
            Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Playing);
            _audioRenderer.SubmitCommand(EngineCommand.ResumeTimeline(transportId,
                _playbackPositionTracker.CaptureAbsoluteOutputFrame() ?? 0));
            StartOutputAndAnchorPosition();
            SchedulePlaybackCompletionPoll();
        }

        public void Stop()
            => Stop(TransportId);

        public void Stop(TransportId transportId)
        {
            if (_isDisposed || transportId != TransportId)
                return;

            lock (_controlStateLock)
                _warmedPosts.Clear();
            RemovePostsOwnedBy(transportId);
            Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Stopped);
            var commandSequence = _audioRenderer.SubmitCommand(EngineCommand.StopTimeline(transportId));
            _playbackPositionTracker.SetPendingPosition(0, commandSequence);
        }

        public void Seek(TransportId transportId, TimeSpan playbackPosition)
        {
            ThrowIfDisposed();
            if (transportId != TransportId)
                return;
            var playbackFrame = PlaybackTime.ToFrames(playbackPosition);
            var commandSequence = _audioRenderer.SubmitCommand(EngineCommand.SeekTimeline(transportId, playbackFrame));
            _playbackPositionTracker.SetPendingPosition(playbackFrame, commandSequence);
        }

        // A stopped post now ramps to silence rather than being cut, so the buffer a late or
        // starved callback might repeat holds silence rather than the audio that was playing an
        // instant earlier. That is what the device stop here used to be working around.
        public void StopPlayingId(PlayingId playingId)
        {
            ThrowIfDisposed();
            lock (_controlStateLock)
            {
                _warmedPosts.RemoveAll(warmedPost => warmedPost.PlayingId == playingId);
                _dynamicPosts.RemoveAll(post => post.PlayingId == playingId);
            }
            _postCompletions.TryRemove(playingId, out _);
            _immediatePlaybackStates.TryRemove(playingId, out _);
            _audioRenderer.SubmitCommand(EngineCommand.StopPlayingId(playingId));
        }

        public void Pause(PlayingId playingId)
        {
            ThrowIfDisposed();
            if (!_immediatePlaybackStates.TryUpdate(playingId, SoundPlaybackState.Paused, SoundPlaybackState.Playing))
                return;
            _audioRenderer.SubmitCommand(EngineCommand.PausePlayingId(playingId));
        }

        public void Resume(PlayingId playingId)
        {
            ThrowIfDisposed();
            if (!_immediatePlaybackStates.TryUpdate(playingId, SoundPlaybackState.Playing, SoundPlaybackState.Paused))
                return;
            _audioRenderer.SubmitCommand(EngineCommand.ResumePlayingId(
                playingId,
                _playbackPositionTracker.CaptureAbsoluteOutputFrame() ?? 0));
            StartOutputAndAnchorPosition();
            SchedulePlaybackCompletionPoll();
        }

        public void Seek(PlayingId playingId, TimeSpan playbackPosition)
        {
            ThrowIfDisposed();
            if (!_immediatePlaybackStates.ContainsKey(playingId))
                return;
            _audioRenderer.SubmitCommand(EngineCommand.SeekPlayingId(playingId, PlaybackTime.ToFrames(playbackPosition)));
        }

        public TimeSpan? GetPosition(PlayingId playingId)
        {
            if (playingId == default)
                return null;
            return _audioRenderer.TryGetPostPosition(
                playingId,
                _playbackPositionTracker.CaptureAbsoluteOutputFrame(),
                out var mixFrame)
                ? PlaybackTime.FromFrames(mixFrame)
                : null;
        }

        private ResolvedEvent Warm(string eventName, GameObjectState gameObjectState)
        {
            if (_eventProcessor != null)
                return _eventProcessor.Warm(eventName, gameObjectState);

            _logger.Here().Warning($"Event '{eventName}' cannot be posted because no sound banks have been loaded into the engine");
            return null;
        }

        private PlayingId NextPlayingId(TransportId transportId)
            => new(Interlocked.Increment(ref _nextPlayingIdentifier), transportId);

        private PostCompletionState TrackCompletion(PlayingId playingId)
        {
            var completion = new PostCompletionState(playingId);
            _postCompletions[playingId] = completion;
            return completion;
        }

        private PostCompletionState CompletionFor(PlayingId playingId)
            => _postCompletions.TryGetValue(playingId, out var completion)
                ? completion
                : new PostCompletionState(playingId);

        private TransportId NextTransportId()
            => new(Interlocked.Increment(ref _nextTransportIdentifier));

        private void RemovePostsOwnedBy(TransportId transportId)
        {
            if (transportId == default)
                return;
            foreach (var playingId in _postCompletions.Keys)
            {
                if (playingId.TransportId == transportId)
                    _postCompletions.TryRemove(playingId, out _);
            }
        }

        private void EnsureOutputDeviceCreated()
        {
            if (_audioOutputDevice.EnsureCreated(_audioRendererSampleProvider))
                _playbackPositionTracker.ResetOutputEpoch();
        }

        // Restarting a stopped output client resets its clock, so the epoch has to be anchored after
        // the restart, not before — but only when a restart actually happened. Nothing stops the
        // device any more, so re-anchoring a device that never stopped would throw the epoch off by
        // however much audio was buffered ahead of it.
        private void StartOutputAndAnchorPosition()
        {
            if (_audioOutputDevice.EnsurePlaying())
                _playbackPositionTracker.ResetOutputEpoch();
        }

        private void PollPlaybackCompletion(object _)
        {
            if (_isDisposed)
                return;
            if (Interlocked.Exchange(ref _isCompletionPollActive, 1) != 0)
                return;
            try
            {
                var absoluteOutputFrame = _playbackPositionTracker.CaptureAbsoluteOutputFrame();
                ReportCompletedPosts(absoluteOutputFrame);
                ApplyPendingTransitions();
                ReportTelemetry();

                var completedTransportId = _audioRenderer.CompletedTransportId;
                if (completedTransportId == default || completedTransportId.Value == Interlocked.Read(ref _lastReportedCompletedTransportIdentifier))
                    return;
                if (_audioRenderer.LastAppliedCommandSequence < _playbackPositionTracker.PendingPositionCommandSequence)
                    return;
                var completedOutputFrame = _audioRenderer.CompletedOutputFrame;
                if (completedOutputFrame >= 0
                    && absoluteOutputFrame.HasValue
                    && absoluteOutputFrame.Value < completedOutputFrame)
                    return;

                Interlocked.Exchange(ref _lastReportedCompletedTransportIdentifier, completedTransportId.Value);
                if (completedTransportId == TransportId)
                {
                    Volatile.Write(ref _requestedPlaybackState, (int)SoundPlaybackState.Stopped);
                    PlaybackCompleted?.Invoke(completedTransportId);
                }
            }
            finally
            {
                Volatile.Write(ref _isCompletionPollActive, 0);
                if (!_isDisposed && HasPlayingPlayback())
                    SchedulePlaybackCompletionPoll();
            }
        }

        // A sound the engine had no room for, or no resident media for, is otherwise
        // indistinguishable from one that was never asked for, so the shortfall is said out loud
        // rather than left to be inferred from a cue that cannot be heard.
        //
        // Everything here is counted on the render thread and formatted on this one. Moving any of
        // it inline would put string formatting inside the output callback, which is the thing the
        // counters exist to detect.
        private void ReportTelemetry()
        {
            var telemetry = Telemetry;

            var unstartedVoiceCount = telemetry.UnstartedVoiceCount;
            var previouslyUnstartedVoiceCount = Interlocked.Exchange(ref _lastReportedUnstartedVoiceCount, unstartedVoiceCount);
            if (unstartedVoiceCount > previouslyUnstartedVoiceCount)
                _logger.Here().Warning(
                    $"{unstartedVoiceCount - previouslyUnstartedVoiceCount} sound(s) could not be played because an applicable voice budget was full; " +
                    $"they will be silent ({unstartedVoiceCount} since playback began, peak {telemetry.PeakSoundingVoiceCount} voices sounding at once)");

            // Not a shortfall: a stolen voice gave way to something more important, which is the
            // pool working. Worth saying because it is the only sign that a sound ended early on
            // purpose rather than by accident.
            var stolenVoiceCount = telemetry.StolenVoiceCount;
            var previouslyStolenVoiceCount = Interlocked.Exchange(ref _lastReportedStolenVoiceCount, stolenVoiceCount);
            if (stolenVoiceCount > previouslyStolenVoiceCount)
                _logger.Here().Information(
                    $"{stolenVoiceCount - previouslyStolenVoiceCount} voice(s) gave up their place to a higher priority sound " +
                    $"({stolenVoiceCount} since playback began)");

            var unscheduledEventCount = telemetry.UnscheduledEventCount;
            var previouslyUnscheduledEventCount = Interlocked.Exchange(ref _lastReportedUnscheduledEventCount, unscheduledEventCount);
            if (unscheduledEventCount > previouslyUnscheduledEventCount)
                _logger.Here().Warning(
                    $"{unscheduledEventCount - previouslyUnscheduledEventCount} event(s) could not be scheduled because the timeline was full; " +
                    $"they will be silent ({unscheduledEventCount} since playback began)");

            // A hard error rather than a shortfall: the audio thread must never decode, so a cue
            // that fires onto media nobody warmed is a gap in the warm logic and is said so.
            var silentCueCount = telemetry.SilentCueCount;
            var previouslySilentCueCount = Interlocked.Exchange(ref _lastReportedSilentCueCount, silentCueCount);
            if (silentCueCount > previouslySilentCueCount)
                _logger.Here().Error(
                    $"{silentCueCount - previouslySilentCueCount} cue(s) fired with no resident media and were dropped; " +
                    $"the warm step missed them ({silentCueCount} since playback began)");

            // A late buffer is what a glitch actually sounds like, and it is the one thing the mix
            // itself cannot show, so the callback is timed against its own deadline.
            var blockOverrunCount = telemetry.BlockOverrunCount;
            var previouslyBlockOverrunCount = Interlocked.Exchange(ref _lastReportedBlockOverrunCount, blockOverrunCount);
            if (blockOverrunCount > previouslyBlockOverrunCount)
                _logger.Here().Error(
                    $"{blockOverrunCount - previouslyBlockOverrunCount} output buffer(s) took longer to render than they last; " +
                    $"the device may have been starved ({blockOverrunCount} since playback began)");

            // How many voices it actually took to play what was asked for. A cue is one post, but a
            // post is as many voices as the walk selected, so this is the difference between a
            // container picking one variation and a container sounding all of them.
            var peakSoundingVoiceCount = telemetry.PeakSoundingVoiceCount;
            if (peakSoundingVoiceCount > _lastReportedPeakSoundingVoiceCount)
            {
                _lastReportedPeakSoundingVoiceCount = peakSoundingVoiceCount;
                _logger.Here().Information($"{peakSoundingVoiceCount} voice(s) sounding at once at the peak");
            }

            var peakVirtualVoiceCount = telemetry.PeakVirtualVoiceCount;
            if (peakVirtualVoiceCount > _lastReportedPeakVirtualVoiceCount)
            {
                _lastReportedPeakVirtualVoiceCount = peakVirtualVoiceCount;
                _logger.Here().Information(
                    $"{peakVirtualVoiceCount} virtual voice(s) at the peak; {telemetry.PeakPhysicalVoiceCount} physical voice(s) at the physical peak");
            }

            // Why a voice stopped occupying a sounding slot, which the peak counts above cannot
            // say: an ancestor limit turned it virtual, its own level fell below the threshold, or
            // the threshold behaviour it authors is to be killed outright. Three causes that look
            // identical from outside and are fixed in three different places.
            var overLimitVirtualisedVoiceCount = telemetry.OverLimitVirtualisedVoiceCount;
            var previouslyOverLimitVirtualisedVoiceCount = Interlocked.Exchange(
                ref _lastReportedOverLimitVirtualisedVoiceCount,
                overLimitVirtualisedVoiceCount);
            if (overLimitVirtualisedVoiceCount > previouslyOverLimitVirtualisedVoiceCount)
                _logger.Here().Information(
                    $"{overLimitVirtualisedVoiceCount - previouslyOverLimitVirtualisedVoiceCount} voice(s) went virtual because an instance limit was reached " +
                    $"({overLimitVirtualisedVoiceCount} since playback began)");

            var thresholdVirtualisedVoiceCount = telemetry.ThresholdVirtualisedVoiceCount;
            var previouslyThresholdVirtualisedVoiceCount = Interlocked.Exchange(
                ref _lastReportedThresholdVirtualisedVoiceCount,
                thresholdVirtualisedVoiceCount);
            if (thresholdVirtualisedVoiceCount > previouslyThresholdVirtualisedVoiceCount)
                _logger.Here().Information(
                    $"{thresholdVirtualisedVoiceCount - previouslyThresholdVirtualisedVoiceCount} voice(s) went virtual below the audibility threshold " +
                    $"({thresholdVirtualisedVoiceCount} since playback began, {telemetry.VoiceThresholdTransitionCount} threshold crossing(s) in all)");

            var thresholdKilledVoiceCount = telemetry.ThresholdKilledVoiceCount;
            var previouslyThresholdKilledVoiceCount = Interlocked.Exchange(
                ref _lastReportedThresholdKilledVoiceCount,
                thresholdKilledVoiceCount);
            if (thresholdKilledVoiceCount > previouslyThresholdKilledVoiceCount)
                _logger.Here().Information(
                    $"{thresholdKilledVoiceCount - previouslyThresholdKilledVoiceCount} voice(s) were killed below the audibility threshold, as they author " +
                    $"({thresholdKilledVoiceCount} since playback began)");

            // Which bus the level is actually coming from. The master peak says the mix is loud;
            // this says where it was loud before the graph summed it, which is the only way to tell
            // an authored bus gain apart from too many voices on one bus.
            var highestBusPeakLevel = telemetry.HighestBusPeakLevel;
            if (highestBusPeakLevel > _lastReportedHighestBusPeakLevel && highestBusPeakLevel > 1f)
            {
                _lastReportedHighestBusPeakLevel = highestBusPeakLevel;
                _logger.Here().Information(
                    $"Bus {telemetry.HighestPeakBusId} peaked at {highestBusPeakLevel:F2}, the highest any bus has reached");
            }

            var unsupportedEffectCount = telemetry.UnsupportedEffectCount;
            var previouslyUnsupportedEffectCount = Interlocked.Exchange(ref _lastReportedUnsupportedEffectCount, unsupportedEffectCount);
            if (unsupportedEffectCount > previouslyUnsupportedEffectCount)
                _logger.Here().Warning(
                    $"{unsupportedEffectCount - previouslyUnsupportedEffectCount} master-mixer effect slot(s) are unsupported and passed through dry");

            var unsupportedAuxiliarySendCount = telemetry.UnsupportedAuxiliarySendCount;
            var previouslyUnsupportedAuxiliarySendCount = Interlocked.Exchange(
                ref _lastReportedUnsupportedAuxiliarySendCount,
                unsupportedAuxiliarySendCount);
            if (unsupportedAuxiliarySendCount > previouslyUnsupportedAuxiliarySendCount)
                _logger.Here().Warning(
                    $"{unsupportedAuxiliarySendCount - previouslyUnsupportedAuxiliarySendCount} auxiliary send(s) are unsupported and were not mixed");

            // Reported as a high water mark rather than a level, because the moment worth knowing
            // about is briefer than the interval this poll runs at.
            var masterPeakLevel = telemetry.MasterPeakLevel;
            if (masterPeakLevel > _lastReportedMasterPeakLevel)
            {
                _lastReportedMasterPeakLevel = masterPeakLevel;
                if (masterPeakLevel > 1f)
                    _logger.Here().Information(
                        $"The mix reached {masterPeakLevel:F2} before limiting, so the limiter is holding back " +
                        $"{20 * Math.Log10(masterPeakLevel):F1} dB at the peak");
            }
        }

        private void ReportCompletedPosts(long? absoluteOutputFrame)
        {
            foreach (var pair in _postCompletions)
            {
                if (!pair.Value.TryGetCompletion(out _, out var completedOutputFrame))
                    continue;
                if (absoluteOutputFrame.HasValue
                    && absoluteOutputFrame.Value < completedOutputFrame)
                    continue;

                if (!_postCompletions.TryRemove(pair.Key, out _))
                    continue;
                _immediatePlaybackStates.TryRemove(pair.Key, out _);
                lock (_controlStateLock)
                    _dynamicPosts.RemoveAll(post => post.PlayingId == pair.Key);

                // Raised outside the lock: a handler is free to call straight back into the engine,
                // and it should not have to do so through a lock this thread already holds.
                PostCompleted?.Invoke(pair.Key);
            }
        }

        private bool HasPlayingPlayback()
            => PlaybackState == SoundPlaybackState.Playing
                || _immediatePlaybackStates.Values.Any(state => state == SoundPlaybackState.Playing);

        private void SchedulePlaybackCompletionPoll()
        {
            if (_isDisposed)
                return;
            try
            {
                _playbackCompletionTimer.Change(PlaybackCompletionPollIntervalMilliseconds, Timeout.Infinite);
            }
            catch (ObjectDisposedException) when (_isDisposed)
            {
            }
        }

        private void StopEverythingSounding()
        {
            Stop(TransportId);
            foreach (var playingId in _immediatePlaybackStates.Keys)
                StopPlayingId(playingId);
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

        public void Dispose()
        {
            if (_isDisposed)
                return;

            // Everything the engine stops on purpose ramps -- the transport, the end of a timeline,
            // a stolen voice. Tearing the device down does not, so anything still sounding would be
            // cut off mid-waveform and clicked. Ask it to stop properly first; the device waits for
            // the ramp to reach the speakers before it goes.
            StopEverythingSounding();

            _isDisposed = true;
            _postCompletions.Clear();
            _immediatePlaybackStates.Clear();
            lock (_controlStateLock)
            {
                _warmedPosts.Clear();
                _dynamicPosts.Clear();
            }
            _playbackCompletionTimer.Dispose();
            _audioOutputDevice.Dispose();
        }
    }
}
