using Editors.Audio.Shared.Wwise.Engine.Hierarchy;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Wwise.Engine.Behaviour
{
    // Event to actions. An event is a list of actions, and the engine carries out the ones that
    // change what is sounding: Play starts the walk at the node it names, Stop, Pause and Resume
    // reach what that node already has sounding, and SetState and SetSwitch move a group.
    //
    // The rest are named and counted rather than approximated. An action that sets a bus volume or
    // seeks a playback is not a Play action wearing a different hat, and walking through it as if it
    // were is what the old resolver did.
    //
    // It also warms a node with no event above it, for a tool holding a container rather than an
    // event. That is the same walk entered one level down rather than a second way of resolving
    // anything: what an event contributes is its action list, and a node has none.
    //
    // Control thread only: this is warming.
    internal sealed class EventProcessor(IHierarchyProvider hierarchyProvider, NodeWalker nodeWalker, EngineTelemetry telemetry)
    {
        private readonly ILogger _logger = Logging.Create<EventProcessor>();
        private readonly IHierarchyProvider _hierarchyProvider = hierarchyProvider;
        private readonly NodeWalker _nodeWalker = nodeWalker;
        private readonly EngineTelemetry _telemetry = telemetry;

        public ResolvedEvent Warm(string eventName, GameObjectState gameObject)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                return null;

            var eventHircItem = _hierarchyProvider.FindEvent(eventName);
            if (eventHircItem == null)
            {
                _logger.Here().Warning($"Event '{eventName}' was not found in any loaded bank");
                return null;
            }

            return WarmEvent(eventHircItem, eventName, gameObject);
        }

        // By id, for a caller that picked an event out of the hierarchy rather than being told its
        // name — the audio explorer clicking one. The same event, reached the other way round.
        public ResolvedEvent Warm(uint eventId, GameObjectState gameObject)
        {
            var eventHircItem = _hierarchyProvider.FindNode(eventId, referringBnkFilePath: null);
            if (eventHircItem == null)
            {
                _logger.Here().Warning($"Event {eventId} was not found in any loaded bank");
                return null;
            }

            return WarmEvent(eventHircItem, _hierarchyProvider.GetName(eventId), gameObject);
        }

        // A node with no event above it, entered at the node itself. Everything below this point is
        // the ordinary walk: the same containers, the same per-instance state, the same media.
        public ResolvedEvent WarmNode(uint nodeId, GameObjectState gameObject)
        {
            var nodeName = _hierarchyProvider.GetName(nodeId);
            var builder = new ResolvedEventBuilder(gameObject.ParameterSource);
            var rootNodeIndex = _nodeWalker.WarmRoot(builder, nodeId, referringBnkFilePath: null, gameObject);
            if (rootNodeIndex >= 0)
                builder.AddRoot(rootNodeIndex);

            return Built(builder, nodeName, gameObject);
        }

        private ResolvedEvent WarmEvent(HircItem eventHircItem, string eventName, GameObjectState gameObject)
        {
            if (eventHircItem is not ICAkEvent actionEventHirc)
            {
                _logger.Here().Warning($"'{eventName}' is a {eventHircItem.HircType}, not an event, so it has no actions to carry out");
                return null;
            }

            var builder = new ResolvedEventBuilder(gameObject.ParameterSource);

            // An action can move a switch or a state group that a later action in the same event
            // then selects on, so the transitions are applied to a warm-time overlay as the list is
            // walked. That is what makes the media for the branch the event will actually reach
            // resident before the cue fires, rather than the branch it was in when warming started.
            gameObject.ClearWarmTimeTransitions();
            try
            {
                foreach (var actionId in actionEventHirc.GetActionIds())
                    WarmAction(builder, eventHircItem, eventName, actionId, gameObject);
            }
            finally
            {
                gameObject.ClearWarmTimeTransitions();
            }

            return Built(builder, eventName, gameObject);
        }

        private void WarmAction(ResolvedEventBuilder builder, HircItem eventHircItem, string eventName, uint actionId, GameObjectState gameObject)
        {
            var actionHircItem = _hierarchyProvider.FindNode(actionId, eventHircItem.BnkFilePath);
            if (actionHircItem is not ICAkAction actionHirc)
                return;

            var actionType = actionHirc.GetActionType();
            var properties = actionHirc.GetProperties();

            // An ALL action names no node: it reaches everything its scope allows, and a zero
            // target is how that is said to the voices.
            var targetNodeId = ReachesEverything(actionType) ? 0u : actionHirc.GetChildId();
            var resolvedKind = ResolvedKindOf(actionType);

            switch (resolvedKind)
            {
                case ResolvedActionKind.Play:
                {
                    var rootNodeIndex = _nodeWalker.WarmRoot(builder, targetNodeId, actionHircItem.BnkFilePath, gameObject);
                    if (rootNodeIndex >= 0)
                        builder.AddAction(PlayAction(targetNodeId, rootNodeIndex, properties));
                    return;
                }

                case ResolvedActionKind.Stop:
                case ResolvedActionKind.Pause:
                case ResolvedActionKind.Resume:
                    builder.AddAction(new ResolvedAction(
                        resolvedKind.Value,
                        actionType,
                        targetNodeId,
                        IsScopedToGameObject(actionType),
                        RootNodeIndex: -1,
                        Milliseconds(properties, WwiseProperty.ActionDelay),
                        Milliseconds(properties, WwiseProperty.TransitionTime),
                        TransitionIndex: -1));
                    return;

                case ResolvedActionKind.Transition:
                    WarmTransition(builder, actionHirc, actionType, properties, gameObject);
                    return;

                default:
                    // Named, counted and left alone. Saying which action of which event was passed
                    // over is what stops a missing behaviour from looking like a missing sound.
                    _telemetry.CountUnsupportedAction();
                    _logger.Here().Information(
                        $"Event '{eventName}' has action {actionId} of type {actionType}, which this engine does not carry out");
                    return;
            }
        }

        private void WarmTransition(
            ResolvedEventBuilder builder,
            ICAkAction actionHirc,
            AkActionType actionType,
            AuthoredProperties properties,
            GameObjectState gameObject)
        {
            var (groupType, groupId, valueId) = actionType == AkActionType.SetState
                ? (AkGroupType.State, actionHirc.GetStateGroupId(), actionHirc.GetTargetStateId())
                : (AkGroupType.Switch, actionHirc.GetSwitchGroupId(), actionHirc.GetSwitchValueId());

            if (groupId == 0)
            {
                _telemetry.CountUnsupportedAction();
                _logger.Here().Information($"A {actionType} action names no group, so nothing was changed");
                return;
            }

            var groupName = _hierarchyProvider.GetName(groupId);
            var valueName = _hierarchyProvider.GetName(valueId);

            // Applied to the overlay now so the rest of this event warms against it, and recorded in
            // the plan so the control thread can apply it for real when the cue actually fires.
            gameObject.ApplyWarmTimeTransition(groupType, groupName, valueName);
            var transitionIndex = builder.AddTransition(new ResolvedTransition(groupType, groupName, valueName));
            builder.AddAction(new ResolvedAction(
                ResolvedActionKind.Transition,
                actionType,
                TargetNodeId: 0,
                IsScopedToGameObject: true,
                RootNodeIndex: -1,
                Milliseconds(properties, WwiseProperty.ActionDelay),
                RandomisedProperty.None,
                transitionIndex));
        }

        private static ResolvedAction PlayAction(uint targetNodeId, int rootNodeIndex, AuthoredProperties properties)
            => new(
                ResolvedActionKind.Play,
                AkActionType.Play,
                targetNodeId,
                IsScopedToGameObject: true,
                rootNodeIndex,
                Milliseconds(properties, WwiseProperty.ActionDelay),
                Milliseconds(properties, WwiseProperty.TransitionTime),
                TransitionIndex: -1);

        // Wwise stores an action's delay and fade as signed millisecond integers, with a range the
        // instance is randomised within. They are kept in milliseconds here and turned into frames
        // at cue time, where the mix rate is what a frame means.
        private static RandomisedProperty Milliseconds(AuthoredProperties properties, WwiseProperty property)
        {
            var value = properties.TryGetValue(property, out var authored) ? authored.Milliseconds : 0;
            if (!properties.TryGetRange(property, out var minimum, out var maximum))
                return new RandomisedProperty(value, 0f, 0f);
            return new RandomisedProperty(value, minimum.Milliseconds, maximum.Milliseconds);
        }

        // What an action does to what is sounding, or null for one this engine leaves alone. Wwise
        // spells the scope into the type: the "_O" variants reach only the game object the event was
        // posted on, the rest reach every emitter.
        private static ResolvedActionKind? ResolvedKindOf(AkActionType actionType) => actionType switch
        {
            AkActionType.Play or AkActionType.PlayAndContinue => ResolvedActionKind.Play,

            AkActionType.Stop_E or AkActionType.Stop_E_O or AkActionType.Stop_ALL or AkActionType.Stop_ALL_O
                or AkActionType.Stop_AE or AkActionType.Stop_AE_O => ResolvedActionKind.Stop,

            AkActionType.Pause_E or AkActionType.Pause_E_O or AkActionType.Pause_ALL or AkActionType.Pause_ALL_O
                or AkActionType.Pause_AE or AkActionType.Pause_AE_O => ResolvedActionKind.Pause,

            AkActionType.Resume_E or AkActionType.Resume_E_O or AkActionType.Resume_ALL or AkActionType.Resume_ALL_O
                or AkActionType.Resume_AE or AkActionType.Resume_AE_O => ResolvedActionKind.Resume,

            AkActionType.SetState or AkActionType.SetSwitch => ResolvedActionKind.Transition,

            _ => null
        };

        private static bool IsScopedToGameObject(AkActionType actionType) => actionType switch
        {
            AkActionType.Stop_E_O or AkActionType.Stop_ALL_O or AkActionType.Stop_AE_O
                or AkActionType.Pause_E_O or AkActionType.Pause_ALL_O or AkActionType.Pause_AE_O
                or AkActionType.Resume_E_O or AkActionType.Resume_ALL_O or AkActionType.Resume_AE_O => true,
            _ => false
        };

        // An action that names no particular node reaches everything the scope allows, which is what
        // the ALL variants mean.
        private static bool ReachesEverything(AkActionType actionType) => actionType switch
        {
            AkActionType.Stop_ALL or AkActionType.Stop_ALL_O
                or AkActionType.Pause_ALL or AkActionType.Pause_ALL_O
                or AkActionType.Resume_ALL or AkActionType.Resume_ALL_O => true,
            _ => false
        };

        private ResolvedEvent Built(ResolvedEventBuilder builder, string name, GameObjectState gameObject)
        {
            var resolvedEvent = builder.Build(name);
            if (resolvedEvent.PlayableSoundCount == 0 && resolvedEvent.Actions.Length == 0)
                _logger.Here().Warning($"'{name}' on '{gameObject.Name}' resolved to no playable audio");
            else
                _logger.Here().Information(
                    $"'{name}' on '{gameObject.Name}' warmed to {resolvedEvent.PlayableSoundCount} candidate sound(s) " +
                    $"across {resolvedEvent.Actions.Length} action(s), longest {resolvedEvent.LongestCandidateDuration.TotalMilliseconds:F0} ms");
            return resolvedEvent;
        }
    }
}
