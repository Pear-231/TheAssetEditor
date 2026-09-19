using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Shared.GameFormats.Wwise.Enums;

namespace Editors.Audio.Shared.Wwise.Engine.Behaviour
{
    internal enum ResolvedNodeKind
    {
        // A sound, with the media it will play already attached.
        Sound,

        // Every child sounds. Layer containers, and a switch branch that names more than one node.
        All,

        // One weighted pick, against the avoid-repeat history.
        Random,

        // The next item in playlist order.
        Sequence
    }

    // What an event's action does, once the parts of it the engine can act on have been worked out.
    // The action types Wwise defines are far more numerous than the ones a replicated engine can
    // carry out, so they are folded down to what actually happens to a voice.
    internal enum ResolvedActionKind
    {
        // Starts sounds under the target node.
        Play,

        // Stops, pauses or resumes what the target node has sounding.
        Stop,
        Pause,
        Resume,

        // Moves a switch or state group, which the control thread applies.
        Transition
    }

    // One action of an event, with everything the audio thread needs to carry it out.
    internal readonly record struct ResolvedAction(
        ResolvedActionKind Kind,
        AkActionType ActionType,
        uint TargetNodeId,

        // Whether the action reaches only what this game object has sounding, or everything. Wwise
        // spells the difference into the action type: the "_O" variants are scoped to the object the
        // event was posted on, the rest reach every emitter.
        bool IsScopedToGameObject,

        // Where in the plan the sounds this action would start are, or -1 for an action that starts
        // nothing.
        int RootNodeIndex,

        // Authored, in milliseconds, and randomised per instance like any other authored property.
        RandomisedProperty DelayMilliseconds,
        RandomisedProperty FadeMilliseconds,

        // Which of the plan's transitions this action performs, or -1.
        int TransitionIndex);

    // A switch or state group an action moves. Held by name because that is what the registry is
    // keyed on, and resolved at warm time so that carrying it out costs the audio thread nothing but
    // handing the name on.
    internal readonly record struct ResolvedTransition(AkGroupType GroupType, string GroupName, string ValueName);

    internal struct ResolvedNode
    {
        public ResolvedNodeKind Kind;
        public uint NodeId;

        // Sound only. Kept alongside the media so callers can say which files an event could reach
        // without the engine having to hand out its media.
        public uint SourceId;

        // Sound only, and null when the bank named a source that could not be found. Left in the
        // plan rather than dropped so a random container can fall past it to the next variation
        // instead of the whole event going silent.
        public SourceMedia Media;

        // Sound only. `Limit` is the compatibility view of the nearest descriptor; the indexed
        // range immediately after it contains every independently enforced ancestor budget.
        public VoiceLimit Limit;
        public int FirstLimitIndex;
        public int LimitCount;

        // Sound only. Volume, pitch and filtering accumulated down the hierarchy, and the priority
        // of the nearest node that overrode it, with the randomiser ranges still open so cue time
        // can draw within them.
        public AuthoredParameters Parameters;
        public AttenuationSettings Attenuation;

        // Sound only. Where this sound sits in the hierarchy, so an action that targets a container
        // can find the voices playing underneath it. Indices into the plan's ancestor array.
        public int FirstAncestorIndex;
        public int AncestorCount;

        public int FirstChildIndex;
        public int ChildCount;

        // Random and Sequence only.
        public PlaybackInstanceState InstanceState;
        public bool IsShuffle;
        public bool IsContinuous;
        public bool AlwaysResetsPlaylist;
        public int LoopCount;
        public int LoopMinimumOffset;
        public int LoopMaximumOffset;
        public AkTransitionMode TransitionMode;
        public RandomisedProperty TransitionMilliseconds;
        public float FadeOutMilliseconds;
        public float FadeInMilliseconds;
    }

    // An event walked against a game object's switch values, with the media for every candidate the
    // pick could land on already decoded and attached.
    //
    // Built on the control thread and then only read on the audio thread. It is a flat array rather
    // than a tree of objects because cue time walks it inside the render callback, where an index
    // loop over arrays is the difference between bounded work and a pointer chase of unknown cost.
    internal sealed class ResolvedEvent
    {
        public ResolvedEvent(
            string eventName,
            ResolvedNode[] nodes,
            int[] childNodeIndices,
            int[] childWeights,
            uint[] ancestorNodeIds,
            VoiceLimit[] voiceLimits,
            ResolvedAction[] actions,
            ResolvedTransition[] transitions)
        {
            EventName = eventName;
            Nodes = nodes;
            ChildNodeIndices = childNodeIndices;
            ChildWeights = childWeights;
            AncestorNodeIds = ancestorNodeIds;
            VoiceLimits = voiceLimits;
            Actions = actions;
            Transitions = transitions;

            var playableSourceIds = new List<uint>();
            foreach (var node in nodes)
            {
                if (node.Kind != ResolvedNodeKind.Sound || node.Media == null)
                    continue;
                playableSourceIds.Add(node.SourceId);
                if (node.Media.Duration > LongestCandidateDuration)
                    LongestCandidateDuration = node.Media.Duration;
            }
            PlayableSourceIds = playableSourceIds;
        }

        public string EventName { get; }

        public ResolvedNode[] Nodes { get; }
        public int[] ChildNodeIndices { get; }
        public int[] ChildWeights { get; }
        public uint[] AncestorNodeIds { get; }
        public VoiceLimit[] VoiceLimits { get; }
        public ResolvedAction[] Actions { get; }
        public ResolvedTransition[] Transitions { get; }
        public GameObjectParameterSource GameObjectParameters { get; init; }
        public bool RequiresContinuousValidation { get; init; }

        // Every sound this post could land on, so a caller can say what an event would reach
        // without one of them having to be picked first.
        public IReadOnlyList<uint> PlayableSourceIds { get; }
        public int PlayableSoundCount => PlayableSourceIds.Count;

        // The longest sound this post could pick, for callers watching whether a cue was heard at
        // all. Which sound actually plays is not known until the cue fires.
        public TimeSpan LongestCandidateDuration { get; }
    }

    internal sealed class ResolvedEventBuilder
    {
        private readonly List<ResolvedNode> _nodes = [];
        private readonly List<int> _childNodeIndices = [];
        private readonly List<int> _childWeights = [];
        private readonly List<uint> _ancestorNodeIds = [];
        private readonly List<VoiceLimit> _voiceLimits = [];
        private readonly List<ResolvedAction> _actions = [];
        private readonly List<ResolvedTransition> _transitions = [];
        private bool _requiresContinuousValidation;

        public ResolvedEventBuilder(GameObjectParameterSource gameObjectParameters = null)
            => GameObjectParameters = gameObjectParameters;

        public GameObjectParameterSource GameObjectParameters { get; }

        public int NodeCount => _nodes.Count;

        public int AddSound(
            uint nodeId,
            uint sourceId,
            SourceMedia media,
            IReadOnlyList<VoiceLimit> limits,
            in AuthoredParameters parameters,
            IReadOnlyList<uint> ancestorNodeIds,
            AttenuationSettings attenuation = null)
        {
            var firstAncestorIndex = _ancestorNodeIds.Count;
            foreach (var ancestorNodeId in ancestorNodeIds)
                _ancestorNodeIds.Add(ancestorNodeId);
            var firstLimitIndex = _voiceLimits.Count;
            foreach (var limit in limits)
                _voiceLimits.Add(limit);

            _nodes.Add(new ResolvedNode
            {
                Kind = ResolvedNodeKind.Sound,
                NodeId = nodeId,
                SourceId = sourceId,
                Media = media,
                Limit = limits.Count == 0 ? VoiceLimit.None : limits[^1],
                FirstLimitIndex = firstLimitIndex,
                LimitCount = _voiceLimits.Count - firstLimitIndex,
                Parameters = parameters,
                Attenuation = attenuation ?? AttenuationSettings.None,
                FirstAncestorIndex = firstAncestorIndex,
                AncestorCount = _ancestorNodeIds.Count - firstAncestorIndex
            });
            return _nodes.Count - 1;
        }

        // Weights are passed alongside rather than folded into the child indices because a weight
        // only means anything in playlist order, and only for a random container.
        public int AddContainer(
            ResolvedNodeKind kind,
            uint nodeId,
            PlaybackInstanceState instanceState,
            List<int> childNodeIndices,
            List<int> childWeights,
            bool isShuffle = false,
            bool isContinuous = false,
            bool alwaysResetsPlaylist = false,
            int loopCount = 1,
            int loopMinimumOffset = 0,
            int loopMaximumOffset = 0,
            AkTransitionMode transitionMode = AkTransitionMode.Disabled,
            RandomisedProperty transitionMilliseconds = default,
            float fadeOutMilliseconds = 0f,
            float fadeInMilliseconds = 0f)
        {
            var firstChildIndex = _childNodeIndices.Count;
            for (var childOrdinal = 0; childOrdinal < childNodeIndices.Count; childOrdinal++)
            {
                _childNodeIndices.Add(childNodeIndices[childOrdinal]);
                _childWeights.Add(childWeights == null ? 0 : childWeights[childOrdinal]);
            }

            _nodes.Add(new ResolvedNode
            {
                Kind = kind,
                NodeId = nodeId,
                FirstChildIndex = firstChildIndex,
                ChildCount = childNodeIndices.Count,
                InstanceState = instanceState,
                IsShuffle = isShuffle,
                IsContinuous = isContinuous,
                AlwaysResetsPlaylist = alwaysResetsPlaylist,
                LoopCount = loopCount,
                LoopMinimumOffset = loopMinimumOffset,
                LoopMaximumOffset = loopMaximumOffset,
                TransitionMode = transitionMode,
                TransitionMilliseconds = transitionMilliseconds,
                FadeOutMilliseconds = fadeOutMilliseconds,
                FadeInMilliseconds = fadeInMilliseconds
            });
            return _nodes.Count - 1;
        }

        // A sound with nothing above it: no hierarchy to accumulate from and no container to hang
        // under, which is what direct entry at a node looks like and what a test builds when the
        // parameters are not the thing it is testing.
        public int AddSound(uint nodeId, uint sourceId, SourceMedia media, VoiceLimit limit)
            => AddSound(nodeId, sourceId, media, limit.IsLimited ? [limit] : [], AuthoredParameters.Inherited, []);

        public void AddAction(in ResolvedAction action) => _actions.Add(action);

        public void RequireContinuousValidation() => _requiresContinuousValidation = true;

        // A root that plays with no action above it — what a caller entering the walk at a node
        // holds, since a node contributes no action list of its own.
        public void AddRoot(int nodeIndex)
            => AddAction(new ResolvedAction(
                ResolvedActionKind.Play,
                AkActionType.Play,
                TargetNodeId: 0,
                IsScopedToGameObject: true,
                nodeIndex,
                RandomisedProperty.None,
                RandomisedProperty.None,
                TransitionIndex: -1));

        public int AddTransition(in ResolvedTransition transition)
        {
            _transitions.Add(transition);
            return _transitions.Count - 1;
        }

        public ResolvedEvent Build(string eventName)
            => new ResolvedEvent(
                eventName,
                [.. _nodes],
                [.. _childNodeIndices],
                [.. _childWeights],
                [.. _ancestorNodeIds],
                [.. _voiceLimits],
                [.. _actions],
                [.. _transitions])
            {
                RequiresContinuousValidation = _requiresContinuousValidation,
                GameObjectParameters = GameObjectParameters
            };
    }
}
