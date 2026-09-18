using Shared.GameFormats.Wwise.Enums;
using System.Numerics;

namespace Editors.Audio.Shared.Wwise.Engine.Behaviour
{
    // The emitters the game layer has told the engine about, and what it has told the engine about
    // each of them.
    //
    // Switches and game parameters are per emitter; states are not. Wwise scopes a state group to
    // the whole game rather than to an object — "combat music on" is true for everything at once —
    // so states live here rather than on a game object, and a switch container that switches on a
    // state group reads them from here.
    //
    // Control side only, and never from the audio thread: what the audio thread reads is the
    // published snapshot, which is immutable once handed over.
    //
    // The control side is more than one thread, though — the game layer calls in on the UI thread
    // and the completion poll applies an event's own SetSwitch and SetState actions on a timer
    // thread — so this type is not itself synchronised and `SoundEngine._controlStateLock` is what
    // keeps the two apart. Anything that reaches these members must hold it.
    internal sealed class GameObjectRegistry
    {
        private readonly Dictionary<long, GameObjectState> _gameObjectsById = [];
        private readonly Dictionary<ContainerInstanceKey, PlaybackInstanceState> _globalInstanceStates = [];

        // Global, and shared by reference with every game object so a container switching on a state
        // group resolves against the same values wherever it is reached from.
        private readonly Dictionary<string, string> _stateValuesByGroupName = new(StringComparer.OrdinalIgnoreCase);

        private long _nextGameObjectIdentifier;

        public GameObjectId Register(string name)
        {
            var gameObjectId = new GameObjectId(++_nextGameObjectIdentifier);
            _gameObjectsById.Add(gameObjectId.Value, new GameObjectState(gameObjectId, name, _stateValuesByGroupName, _globalInstanceStates));
            return gameObjectId;
        }

        public void Unregister(GameObjectId gameObjectId)
            => _gameObjectsById.Remove(gameObjectId.Value);

        public GameObjectState Find(GameObjectId gameObjectId)
            => _gameObjectsById.GetValueOrDefault(gameObjectId.Value);

        public IEnumerable<GameObjectState> All => _gameObjectsById.Values;

        public bool SetPosition(GameObjectId gameObjectId, Vector3 position)
        {
            if (!IsFinite(position) || Find(gameObjectId) is not { } gameObject || gameObject.Position == position)
                return false;
            gameObject.Position = position;
            PublishSpatialParameters();
            return true;
        }

        public bool SetListeners(GameObjectId gameObjectId, IReadOnlyList<GameObjectId> listenerIds)
        {
            if (Find(gameObjectId) is not { } gameObject)
                return false;
            gameObject.SetListeners(listenerIds);
            PublishSpatialParameters();
            return true;
        }

        // Returns whether anything changed, so a caller only re-warms when a state actually moved.
        public bool SetState(string stateGroupName, string stateValueName)
        {
            if (string.IsNullOrWhiteSpace(stateGroupName))
                return false;
            if (_stateValuesByGroupName.TryGetValue(stateGroupName, out var currentStateValue)
                && string.Equals(currentStateValue, stateValueName, StringComparison.OrdinalIgnoreCase))
                return false;

            _stateValuesByGroupName[stateGroupName] = stateValueName;
            return true;
        }

        public bool TryGetState(string stateGroupName, out string stateValueName)
            => _stateValuesByGroupName.TryGetValue(stateGroupName, out stateValueName);

        private void PublishSpatialParameters()
        {
            foreach (var gameObject in _gameObjectsById.Values)
            {
                var listenerPositions = new Vector3[Math.Min(8, gameObject.ListenerIds.Count)];
                for (var listenerOrdinal = 0; listenerOrdinal < listenerPositions.Length; listenerOrdinal++)
                    listenerPositions[listenerOrdinal] = Find(gameObject.ListenerIds[listenerOrdinal])?.Position ?? Vector3.Zero;
                gameObject.PublishParameters(listenerPositions);
            }
        }

        private static bool IsFinite(Vector3 position)
            => float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
    }

    // What one emitter carries, and the snapshot of it the audio thread is allowed to read.
    //
    // The snapshot is rebuilt whole on every change rather than mutated, because the render callback
    // may be reading the last one at the moment the control thread writes the next: publishing a new
    // immutable object and swapping the reference is what makes that safe without a lock.
    internal sealed class GameObjectParameters
    {
        public static readonly GameObjectParameters Empty = new([], Vector3.Zero, []);
        private readonly KeyValuePair<string, float>[] _gameParameterValues;

        public GameObjectParameters(
            KeyValuePair<string, float>[] gameParameterValues,
            Vector3 position = default,
            Vector3[] listenerPositions = null)
        {
            _gameParameterValues = gameParameterValues;
            Position = position;
            ListenerPositions = listenerPositions ?? [];
        }

        public Vector3 Position { get; }
        public ReadOnlyMemory<Vector3> ListenerPositions { get; }

        public bool TryGetGameParameter(string gameParameterName, out float value)
        {
            foreach (var (name, gameParameterValue) in _gameParameterValues)
            {
                if (!string.Equals(name, gameParameterName, StringComparison.OrdinalIgnoreCase))
                    continue;

                value = gameParameterValue;
                return true;
            }

            value = 0f;
            return false;
        }

        public int Count => _gameParameterValues.Length;
    }

    // Stable identity held by active voices; each published value remains immutable for readers
    // already holding it, while a moving emitter can replace what the source points at.
    internal sealed class GameObjectParameterSource
    {
        private GameObjectParameters _current = GameObjectParameters.Empty;
        public GameObjectParameters Current => Volatile.Read(ref _current);
        public void Publish(GameObjectParameters parameters) => Volatile.Write(ref _current, parameters);
    }

    internal sealed class GameObjectState
    {
        // Switch values are held by name because that is what both the game layer and the HIRC tree
        // can be compared on: the containers carry ids, and the repository is the only thing that
        // can turn one into the other.
        private readonly Dictionary<string, string> _switchValuesByGroupName = new(StringComparer.OrdinalIgnoreCase);

        // The state values, which are the whole game's rather than this object's. Held by reference
        // so a state set after this object was registered is still the one it resolves against.
        private readonly Dictionary<string, string> _stateValuesByGroupName;

        // What a SetSwitch or SetState action in an event has said, for as long as the rest of that
        // event is being warmed. An action can change the branch a later action in the same event
        // selects, so the media for the branch it will actually reach has to be warmed against the
        // value the action set, not the one the object had when the event started.
        private readonly Dictionary<string, string> _warmTimeSwitchOverlay = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _warmTimeStateOverlay = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, float> _gameParameterValues = new(StringComparer.OrdinalIgnoreCase);

        // Shared by every warmed plan on this game object, so a container that two events both
        // reach keeps one sequence cursor and one avoid-repeat history, which is what per node and
        // per game object means.
        private readonly Dictionary<ContainerInstanceKey, PlaybackInstanceState> _instanceStates = [];
        private readonly Dictionary<ContainerInstanceKey, PlaybackInstanceState> _globalInstanceStates;

        private GameObjectParameters _publishedParameters = GameObjectParameters.Empty;
        private readonly GameObjectParameterSource _parameterSource = new();
        private readonly List<GameObjectId> _listenerIds = [];

        public GameObjectState(
            GameObjectId id,
            string name,
            Dictionary<string, string> stateValuesByGroupName,
            Dictionary<ContainerInstanceKey, PlaybackInstanceState> globalInstanceStates)
        {
            Id = id;
            Name = name;
            _stateValuesByGroupName = stateValuesByGroupName;
            _globalInstanceStates = globalInstanceStates;
        }

        public GameObjectId Id { get; }
        public string Name { get; }
        public Vector3 Position { get; set; }
        public IReadOnlyList<GameObjectId> ListenerIds => _listenerIds;

        // Read from the render callback, written from the control thread, swapped whole.
        public GameObjectParameters PublishedParameters => Volatile.Read(ref _publishedParameters);
        public GameObjectParameterSource ParameterSource => _parameterSource;

        public void SetListeners(IReadOnlyList<GameObjectId> listenerIds)
        {
            _listenerIds.Clear();
            for (var listenerOrdinal = 0; listenerOrdinal < listenerIds.Count && _listenerIds.Count < 8; listenerOrdinal++)
            {
                if (listenerIds[listenerOrdinal] != Id && !_listenerIds.Contains(listenerIds[listenerOrdinal]))
                    _listenerIds.Add(listenerIds[listenerOrdinal]);
            }
        }

        public bool SetSwitch(string switchGroupName, string switchValueName)
        {
            if (string.IsNullOrWhiteSpace(switchGroupName))
                return false;
            if (_switchValuesByGroupName.TryGetValue(switchGroupName, out var currentSwitchValue)
                && string.Equals(currentSwitchValue, switchValueName, StringComparison.OrdinalIgnoreCase))
                return false;

            _switchValuesByGroupName[switchGroupName] = switchValueName;
            return true;
        }

        public bool SetGameParameter(string gameParameterName, float value)
        {
            if (string.IsNullOrWhiteSpace(gameParameterName))
                return false;
            if (_gameParameterValues.TryGetValue(gameParameterName, out var currentValue) && currentValue == value)
                return false;

            _gameParameterValues[gameParameterName] = value;
            PublishParameters();
            return true;
        }

        // The value a container should switch on, which depends on what kind of group it switches
        // on: a switch group is this object's, a state group is the game's.
        public bool TryGetGroupValue(AkGroupType groupType, string groupName, out string groupValueName)
        {
            if (groupName == null)
            {
                groupValueName = null;
                return false;
            }

            var overlay = groupType == AkGroupType.State ? _warmTimeStateOverlay : _warmTimeSwitchOverlay;
            if (overlay.TryGetValue(groupName, out groupValueName))
                return true;

            return groupType == AkGroupType.State
                ? _stateValuesByGroupName.TryGetValue(groupName, out groupValueName)
                : _switchValuesByGroupName.TryGetValue(groupName, out groupValueName);
        }

        public void ApplyWarmTimeTransition(AkGroupType groupType, string groupName, string groupValueName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return;

            var overlay = groupType == AkGroupType.State ? _warmTimeStateOverlay : _warmTimeSwitchOverlay;
            overlay[groupName] = groupValueName;
        }

        public void ClearWarmTimeTransitions()
        {
            _warmTimeSwitchOverlay.Clear();
            _warmTimeStateOverlay.Clear();
        }

        public PlaybackInstanceState GetInstanceState(
            string bankPath,
            uint nodeId,
            int avoidRepeatCount,
            int childCount,
            bool isGlobal,
            bool isShuffle)
        {
            var key = new ContainerInstanceKey(bankPath, nodeId);
            var states = isGlobal ? _globalInstanceStates : _instanceStates;
            if (states.TryGetValue(key, out var instanceState))
                return instanceState;

            instanceState = new PlaybackInstanceState(key, avoidRepeatCount, childCount, isShuffle);
            states.Add(key, instanceState);
            return instanceState;
        }

        public void PublishParameters(Vector3[] listenerPositions)
            => Publish(new GameObjectParameters([.. _gameParameterValues], Position, listenerPositions));

        private void PublishParameters()
            => Publish(new GameObjectParameters([.. _gameParameterValues], Position, [.. PublishedParameters.ListenerPositions.Span]));

        private void Publish(GameObjectParameters parameters)
        {
            Volatile.Write(ref _publishedParameters, parameters);
            _parameterSource.Publish(parameters);
        }
    }


    internal readonly record struct ContainerInstanceKey(string BankPath, uint NodeId);
}
