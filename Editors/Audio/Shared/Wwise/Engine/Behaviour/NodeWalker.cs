using Editors.Audio.Shared.Wwise.Engine.Hierarchy;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Timing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Wwise.Engine.Behaviour
{
    // One sound the walk landed on. A post can produce several of these: every layer of a layer
    // container sounds at once.
    //
    // The ancestors travel with it because an action that stops a container has to find the voices
    // playing underneath it, and the audio thread cannot walk the hierarchy to work out what is
    // underneath what.
    internal readonly record struct SelectedSound(
        uint NodeId,
        SourceMedia Media,
        VoiceLimit Limit,
        ReadOnlyMemory<VoiceLimit> Limits,
        AuthoredParameters Parameters,
        ReadOnlyMemory<uint> AncestorNodeIds,
        int ContainerDelayFrames = 0,
        int ContainerFadeInFrames = 0,
        int ContainerFadeOutFrames = 0,
        int ContainerEndFadeOutFrames = 0,
        bool UsesEqualPowerCrossfade = false,
        int LoopCount = 1,
        AttenuationSettings Attenuation = null,
        GameObjectParameterSource GameObjectParameters = null,
        bool IsPositioned = false,
        byte VirtualQueueBehaviour = 2,
        byte BelowThresholdBehaviour = 0,
        int ContinuationNodeIndex = -1,
        int ContinuationFrames = 0);

    // Container resolution, in two halves that run on different threads.
    //
    // Warming walks the HIRC tree against a game object's switch values and writes down every
    // candidate the pick could land on, with its media already decoded and the properties of every
    // node above it already accumulated. That happens on the control thread, where a dictionary
    // lookup, a decode and an allocation all cost nothing.
    //
    // Selecting runs at cue time, inside the render callback, and reads only the plan warming left
    // behind: no hierarchy lookup, no media cache, no allocation. That split is what lets a
    // container pick fresh on every pass without putting a decode on the audio thread.
    internal sealed class NodeWalker(IHierarchyProvider hierarchyProvider)
    {
        // A HIRC graph can be cyclic and nothing in the bank format forbids it, so the walk is
        // capped and says so rather than running until it stops. A dropped event is recoverable;
        // a walk that never returns is not — even on the control thread it would hang the editor.
        private const int MaximumWalkDepth = 32;
        private const int MaximumWarmedNodeCount = 2_048;

        // The same cap applied upwards. A sound's ancestors are what its volume and priority come
        // from, and a parent chain that loops would otherwise accumulate for ever.
        private const int MaximumAncestorDepth = 32;

        private readonly ILogger _logger = Logging.Create<NodeWalker>();
        private readonly IHierarchyProvider _hierarchyProvider = hierarchyProvider;

        // The ids on the current path, so a node reachable by two routes resolves on both while a
        // node that reaches itself does not. A visited set shared across the whole walk is the
        // natural way to write this and is wrong: one sound referenced by two layers has to sound
        // twice. Control thread only, like the rest of warming.
        //
        // It starts at the topmost ancestor rather than at the walk root, so a sound knows every
        // node it hangs under and not merely the part of the tree this event entered at.
        private readonly List<uint> _pathNodeIds = [];

        // Scratch for the walk up the parent chain, so accumulating downwards can start from the top.
        private readonly List<ICAkParameterNode> _ancestorNodes = [];

        public int WarmRoot(ResolvedEventBuilder builder, uint nodeId, string referringBnkFilePath, GameObjectState gameObject)
        {
            _pathNodeIds.Clear();
            var inheritedParameters = AuthoredParameters.Inherited;
            var inheritedLimits = new List<VoiceLimit>();
            AccumulateAncestors(nodeId, referringBnkFilePath, ref inheritedParameters, inheritedLimits);
            return Warm(builder, nodeId, referringBnkFilePath, gameObject, inheritedParameters, inheritedLimits, depth: 0);
        }

        // Everything above the node the event points at. Wwise accumulates a sound's volume and
        // pitch from the whole actor-mixer hierarchy, not merely from the part of it an event
        // happened to enter, so a container's own parent has to be read even though the walk down
        // never visits it.
        private void AccumulateAncestors(
            uint nodeId,
            string referringBnkFilePath,
            ref AuthoredParameters parameters,
            List<VoiceLimit> limits)
        {
            _ancestorNodes.Clear();

            var hircItem = _hierarchyProvider.FindNode(nodeId, referringBnkFilePath);
            var parentId = (hircItem as ICAkParameterNode)?.GetDirectParentId() ?? 0;
            var referringPath = hircItem?.BnkFilePath ?? referringBnkFilePath;

            while (parentId != 0 && _ancestorNodes.Count < MaximumAncestorDepth)
            {
                var parentHircItem = _hierarchyProvider.FindNode(parentId, referringPath);
                if (parentHircItem is not ICAkParameterNode parentNode)
                    break;

                _ancestorNodes.Add(parentNode);
                referringPath = parentHircItem.BnkFilePath;
                var grandparentId = parentNode.GetDirectParentId();
                if (grandparentId == parentId)
                    break;
                parentId = grandparentId;
            }

            // Top down, so the nearest ancestor is the last to have its say — which is what makes
            // an override an override.
            for (var ancestorOrdinal = _ancestorNodes.Count - 1; ancestorOrdinal >= 0; ancestorOrdinal--)
            {
                var ancestorNode = _ancestorNodes[ancestorOrdinal];
                parameters = parameters.Accumulate(ancestorNode);
                AddLimit(ancestorNode, limits);
                _pathNodeIds.Add(((HircItem)ancestorNode).Id);
            }
        }

        // Returns the plan index of the warmed node, or -1 when nothing beneath it can sound.
        private int Warm(
            ResolvedEventBuilder builder,
            uint nodeId,
            string referringBnkFilePath,
            GameObjectState gameObject,
            AuthoredParameters inheritedParameters,
            IReadOnlyList<VoiceLimit> inheritedLimits,
            int depth)
        {
            if (nodeId == 0)
                return -1;

            if (depth > MaximumWalkDepth || builder.NodeCount >= MaximumWarmedNodeCount)
            {
                _logger.Here().Warning(
                    $"Stopped walking at '{_hierarchyProvider.GetName(nodeId)}' after {depth} levels and {builder.NodeCount} nodes; " +
                    "the rest of this branch will be silent");
                return -1;
            }

            if (_pathNodeIds.Contains(nodeId))
            {
                _logger.Here().Warning($"'{_hierarchyProvider.GetName(nodeId)}' refers back to itself, so the walk stopped there");
                return -1;
            }

            var hircItem = _hierarchyProvider.FindNode(nodeId, referringBnkFilePath);
            if (hircItem == null)
            {
                _logger.Here().Warning($"'{_hierarchyProvider.GetName(nodeId)}' was referenced but is not in any loaded bank");
                return -1;
            }

            var parameters = hircItem is ICAkParameterNode parameterNode
                ? inheritedParameters.Accumulate(parameterNode)
                : inheritedParameters;
            var limitsInForce = LimitsFor(hircItem as ICAkParameterNode, inheritedLimits);

            _pathNodeIds.Add(nodeId);
            try
            {
                return WarmHirc(builder, hircItem, gameObject, parameters, limitsInForce, depth);
            }
            finally
            {
                _pathNodeIds.RemoveAt(_pathNodeIds.Count - 1);
            }
        }

        private int WarmHirc(
            ResolvedEventBuilder builder,
            HircItem hircItem,
            GameObjectState gameObject,
            in AuthoredParameters parameters,
            IReadOnlyList<VoiceLimit> limitsInForce,
            int depth)
        {
            switch (hircItem)
            {
                case ICAkSound soundHirc:
                    return WarmSound(builder, hircItem, soundHirc, parameters, limitsInForce);

                // Warmed by the branch the game object's switch values select, not exhaustively.
                // A switch container over random containers is why "everything reachable" explodes.
                case ICAkSwitchCntr switchContainerHirc:
                    var selectedSwitchChildren = SelectedSwitchChildren(switchContainerHirc, gameObject);
                    var switchFadeOutMilliseconds = 0f;
                    var switchFadeInMilliseconds = 0f;
                    foreach (var childParameter in switchContainerHirc.GetNodeParameters())
                    {
                        if (!selectedSwitchChildren.Contains(childParameter.NodeId))
                            continue;
                        switchFadeOutMilliseconds = Math.Max(switchFadeOutMilliseconds, childParameter.FadeOutTime);
                        switchFadeInMilliseconds = Math.Max(switchFadeInMilliseconds, childParameter.FadeInTime);
                    }
                    if (switchContainerHirc.GetIsContinuousValidation())
                        builder.RequireContinuousValidation();
                    return WarmChildren(
                        builder,
                        ResolvedNodeKind.All,
                        hircItem,
                        selectedSwitchChildren,
                        playlist: null,
                        gameObject,
                        parameters,
                        limitsInForce,
                        depth,
                        switchFadeOutMilliseconds,
                        switchFadeInMilliseconds);

                case ICAkLayerCntr layerContainerHirc:
                    return WarmLayerContainer(builder, hircItem, layerContainerHirc, gameObject, parameters, limitsInForce, depth);

                case ICAkRanSeqCntr randomSequenceContainerHirc:
                    return WarmRandomSequenceContainer(builder, hircItem, randomSequenceContainerHirc, gameObject, parameters, limitsInForce, depth);

                case ICAkActorMixer:
                    // Wwise 2019.2 reports actor mixers as non-playable and rejects them as Play
                    // action targets. They contribute inherited parameters and limits while the
                    // walk climbs through them, but do not invent a child-selection rule.
                    return -1;

                default:
                    return -1;
            }
        }

        // Kept in the plan even when its media is missing, so a container can fall past it to the
        // next variation. Named here rather than counted at cue time, because this is the only
        // place that knows which sound it was.
        private int WarmSound(
            ResolvedEventBuilder builder,
            HircItem hircItem,
            ICAkSound soundHirc,
            in AuthoredParameters parameters,
            IReadOnlyList<VoiceLimit> limitsInForce)
        {
            var sourceId = soundHirc.GetSourceId();
            var media = _hierarchyProvider.FindMedia(sourceId, hircItem.BnkFilePath);
            if (media == null)
                _logger.Here().Warning(
                    $"'{_hierarchyProvider.GetName(hircItem.Id)}' plays {sourceId}.wem, which could not be loaded, so that variation will be skipped");
            var attenuation = ResolveAttenuation(parameters.AttenuationId, hircItem.BnkFilePath);
            return builder.AddSound(hircItem.Id, sourceId, media, limitsInForce, parameters, _pathNodeIds, attenuation);
        }

        private AttenuationSettings ResolveAttenuation(uint attenuationId, string referringBnkFilePath)
        {
            if (attenuationId == 0 || _hierarchyProvider.FindNode(attenuationId, referringBnkFilePath) is not ICAkAttenuation attenuation)
                return AttenuationSettings.None;
            return new AttenuationSettings(
                attenuation.GetCurve(AttenuationCurveType.Volume),
                attenuation.GetCurve(AttenuationCurveType.LowPassFilter),
                attenuation.GetCurve(AttenuationCurveType.HighPassFilter));
        }

        private int WarmRandomSequenceContainer(
            ResolvedEventBuilder builder,
            HircItem hircItem,
            ICAkRanSeqCntr randomSequenceContainerHirc,
            GameObjectState gameObject,
            in AuthoredParameters parameters,
            IReadOnlyList<VoiceLimit> limitsInForce,
            int depth)
        {
            var playlist = randomSequenceContainerHirc.GetPlaylist();
            var containerKind = randomSequenceContainerHirc.GetContainerMode() == AkContainerMode.Sequence
                ? ResolvedNodeKind.Sequence
                : ResolvedNodeKind.Random;
            var childIds = new List<uint>(playlist.Count);
            foreach (var playlistItem in playlist)
                childIds.Add(playlistItem.PlayId);

            return WarmChildren(builder, containerKind, hircItem, childIds, playlist, gameObject, parameters, limitsInForce, depth);
        }

        private int WarmLayerContainer(
            ResolvedEventBuilder builder,
            HircItem hircItem,
            ICAkLayerCntr layerContainer,
            GameObjectState gameObject,
            in AuthoredParameters parameters,
            IReadOnlyList<VoiceLimit> limitsInForce,
            int depth)
        {
            if (layerContainer.GetIsContinuousValidation())
                builder.RequireContinuousValidation();
            var childNodeIndices = new List<int>();
            foreach (var childId in layerContainer.GetChildren())
            {
                var gainDecibels = LayerGain(layerContainer, childId, gameObject);
                if (float.IsNegativeInfinity(gainDecibels) || gainDecibels <= AudioLevel.SilenceDecibels)
                    continue;

                var childNodeIndex = Warm(
                    builder,
                    childId,
                    hircItem.BnkFilePath,
                    gameObject,
                    parameters.AddVolume(gainDecibels),
                    limitsInForce,
                    depth + 1);
                if (childNodeIndex >= 0)
                    childNodeIndices.Add(childNodeIndex);
            }

            return childNodeIndices.Count == 0
                ? -1
                : builder.AddContainer(ResolvedNodeKind.All, hircItem.Id, null, childNodeIndices, null);
        }

        private float LayerGain(ICAkLayerCntr layerContainer, uint childId, GameObjectState gameObject)
        {
            if (layerContainer.GetLayers().Count == 0)
                return 0f;

            var gainDecibels = 0f;
            var hasAssociation = false;
            foreach (var layer in layerContainer.GetLayers())
            {
                if (layer.RtpcType != AkRtpcType.GameParameter)
                    continue;

                var parameterName = _hierarchyProvider.GetName(layer.RtpcId);
                gameObject.PublishedParameters.TryGetGameParameter(parameterName, out var parameterValue);
                foreach (var association in layer.GetAssociatedChildren())
                {
                    if (association.AssociatedChildId != childId)
                        continue;

                    hasAssociation = true;
                    gainDecibels += RtpcCurve.Evaluate(association.GetCurvePoints(), parameterValue);
                }
            }

            return hasAssociation ? gainDecibels : float.NegativeInfinity;
        }

        private int WarmChildren(
            ResolvedEventBuilder builder,
            ResolvedNodeKind containerKind,
            HircItem hircItem,
            List<uint> childIds,
            IReadOnlyList<ICAkRanSeqCntr.IAkPlaylistItem> playlist,
            GameObjectState gameObject,
            AuthoredParameters parameters,
            IReadOnlyList<VoiceLimit> limitsInForce,
            int depth,
            float fadeOutMilliseconds = 0f,
            float fadeInMilliseconds = 0f)
        {
            var childNodeIndices = new List<int>(childIds.Count);
            var childWeights = playlist == null ? null : new List<int>(childIds.Count);

            for (var childOrdinal = 0; childOrdinal < childIds.Count; childOrdinal++)
            {
                var childNodeIndex = Warm(builder, childIds[childOrdinal], hircItem.BnkFilePath, gameObject, parameters, limitsInForce, depth + 1);
                if (childNodeIndex < 0)
                    continue;

                childNodeIndices.Add(childNodeIndex);
                childWeights?.Add(playlist[childOrdinal].Weight);
            }

            if (childNodeIndices.Count == 0)
                return -1;

            var randomSequenceContainer = hircItem as ICAkRanSeqCntr;
            var isShuffle = randomSequenceContainer?.GetRandomMode() == AkRandomMode.Shuffle;
            var instanceState = containerKind is ResolvedNodeKind.Random or ResolvedNodeKind.Sequence
                ? gameObject.GetInstanceState(
                    hircItem.BnkFilePath,
                    hircItem.Id,
                    AvoidRepeatCount(hircItem, childNodeIndices.Count),
                    childNodeIndices.Count,
                    randomSequenceContainer?.GetIsGlobal() == true,
                    isShuffle)
                : null;
            return builder.AddContainer(
                containerKind,
                hircItem.Id,
                instanceState,
                childNodeIndices,
                childWeights,
                isShuffle,
                randomSequenceContainer?.GetIsContinuous() == true,
                randomSequenceContainer?.GetAlwaysResetsPlaylist() == true,
                randomSequenceContainer?.GetLoopCount() ?? 1,
                randomSequenceContainer?.GetLoopMinimumOffset() ?? 0,
                randomSequenceContainer?.GetLoopMaximumOffset() ?? 0,
                randomSequenceContainer?.GetTransitionMode() ?? AkTransitionMode.Disabled,
                randomSequenceContainer == null
                    ? RandomisedProperty.None
                    : new RandomisedProperty(
                        randomSequenceContainer.GetTransitionTime(),
                        randomSequenceContainer.GetTransitionTimeMinimumOffset(),
                        randomSequenceContainer.GetTransitionTimeMaximumOffset()),
                fadeOutMilliseconds,
                fadeInMilliseconds);
        }

        // Every non-zero authored limit is an independent budget. The advanced-settings bits
        // describe its scope and reached behaviour; they do not replace an ancestor's budget.
        private static IReadOnlyList<VoiceLimit> LimitsFor(ICAkParameterNode parameterNode, IReadOnlyList<VoiceLimit> inheritedLimits)
        {
            if (parameterNode == null)
                return inheritedLimits;
            var maximumInstanceCount = parameterNode.GetMaxInstanceCount();
            if (maximumInstanceCount == 0)
                return inheritedLimits;

            var limits = new List<VoiceLimit>(inheritedLimits.Count + 1);
            limits.AddRange(inheritedLimits);
            limits.Add(LimitFor(parameterNode));
            return limits;
        }

        private static void AddLimit(ICAkParameterNode parameterNode, List<VoiceLimit> limits)
        {
            if (parameterNode.GetMaxInstanceCount() == 0)
                return;
            limits.Add(LimitFor(parameterNode));
        }

        private static VoiceLimit LimitFor(ICAkParameterNode parameterNode)
        {
            var hircItem = (HircItem)parameterNode;
            return new VoiceLimit(
                hircItem.Id,
                parameterNode.GetMaxInstanceCount(),
                hircItem.BnkFilePath,
                parameterNode.GetIsGlobalLimit() ? VoiceLimitScope.Global : VoiceLimitScope.GameObject,
                parameterNode.GetDiscardsNewestOnLimit()
                    ? VoiceLimitReachedBehaviour.DiscardNewest
                    : VoiceLimitReachedBehaviour.DiscardOldest,
                parameterNode.GetUsesVirtualVoiceOnLimit()
                    ? VoiceOverLimitBehaviour.Virtualise
                    : VoiceOverLimitBehaviour.Kill);
        }

        // Capped below the playlist length: avoiding every entry would leave nothing to pick.
        private static int AvoidRepeatCount(HircItem hircItem, int childCount)
            => hircItem is ICAkRanSeqCntr randomSequenceContainerHirc
                ? Math.Clamp(randomSequenceContainerHirc.GetAvoidRepeatCount(), 0, Math.Max(0, childCount - 1))
                : 0;

        // The branch the game object is in, falling back to the container's own default when the
        // game layer has said nothing about that group. A container can switch on a state group
        // rather than a switch group, and then the value comes from the game rather than the object.
        private List<uint> SelectedSwitchChildren(ICAkSwitchCntr switchContainerHirc, GameObjectState gameObject)
        {
            var groupType = switchContainerHirc.GetGroupType();
            var groupName = _hierarchyProvider.GetName(switchContainerHirc.GroupId);
            var selectedSwitchValueId = switchContainerHirc.DefaultSwitch;
            if (gameObject.TryGetGroupValue(groupType, groupName, out var selectedSwitchValueName))
            {
                var matchingSwitchValue = switchContainerHirc.SwitchList.FirstOrDefault(switchValue =>
                    string.Equals(_hierarchyProvider.GetName(switchValue.SwitchId), selectedSwitchValueName, StringComparison.OrdinalIgnoreCase));
                if (matchingSwitchValue != null)
                    selectedSwitchValueId = matchingSwitchValue.SwitchId;
                else
                    _logger.Here().Warning(
                        $"{groupType} group '{groupName}' is set to '{selectedSwitchValueName}', which this container has no branch for; " +
                        $"falling back to '{_hierarchyProvider.GetName(selectedSwitchValueId)}'");
            }

            return switchContainerHirc.SwitchList
                .FirstOrDefault(switchValue => switchValue.SwitchId == selectedSwitchValueId)?
                .NodeIdList ?? [];
        }

        // Cue time. Runs on the audio thread: no allocation, no lookup, and bounded by the plan it
        // was handed. Returns how many sounds it placed in the destination.
        //
        // Static deliberately. Selecting reads nothing but the plan and the instance state hanging
        // off it, and saying so here is what keeps the hierarchy, the media cache and the warm-time
        // scratch state out of reach of the render callback.
        public static int Select(ResolvedEvent resolvedEvent, SelectedSound[] destination)
            => Select(resolvedEvent, destination, out _);

        // Every sound the whole plan can reach, for a caller holding a node rather than an event.
        public static int Select(ResolvedEvent resolvedEvent, SelectedSound[] destination, out int droppedSoundCount)
        {
            var selectedCount = 0;
            droppedSoundCount = 0;
            foreach (var action in resolvedEvent.Actions)
            {
                if (action.Kind == ResolvedActionKind.Play && action.RootNodeIndex >= 0)
                    SelectNode(resolvedEvent, action.RootNodeIndex, destination, ref selectedCount, ref droppedSoundCount);
            }
            return selectedCount;
        }

        // One action's worth, because each action of an event has its own delay, its own fade and
        // its own completion to answer for.
        public static int SelectFromRoot(
            ResolvedEvent resolvedEvent,
            int rootNodeIndex,
            SelectedSound[] destination,
            out int droppedSoundCount)
        {
            var selectedCount = 0;
            droppedSoundCount = 0;
            if (rootNodeIndex >= 0)
                SelectNode(resolvedEvent, rootNodeIndex, destination, ref selectedCount, ref droppedSoundCount);
            return selectedCount;
        }

        // Returns whether anything beneath this node sounded, which is what lets a container fall
        // past a variation whose media is missing instead of the whole cue going silent.
        private static bool SelectNode(
            ResolvedEvent resolvedEvent,
            int nodeIndex,
            SelectedSound[] destination,
            ref int selectedCount,
            ref int droppedSoundCount)
        {
            ref var node = ref resolvedEvent.Nodes[nodeIndex];
            switch (node.Kind)
            {
                case ResolvedNodeKind.Sound:
                    if (node.Media == null)
                        return false;
                    if (selectedCount >= destination.Length)
                    {
                        droppedSoundCount++;
                        return true;
                    }
                    destination[selectedCount++] = new SelectedSound(
                        node.NodeId,
                        node.Media,
                        node.Limit,
                        new ReadOnlyMemory<VoiceLimit>(resolvedEvent.VoiceLimits, node.FirstLimitIndex, node.LimitCount),
                        node.Parameters,
                        new ReadOnlyMemory<uint>(resolvedEvent.AncestorNodeIds, node.FirstAncestorIndex, node.AncestorCount),
                        LoopCount: node.Parameters.LoopCount,
                        Attenuation: node.Attenuation,
                        GameObjectParameters: resolvedEvent.GameObjectParameters,
                        IsPositioned: node.Parameters.IsPositioned,
                        VirtualQueueBehaviour: node.Parameters.VirtualQueueBehaviour,
                        BelowThresholdBehaviour: node.Parameters.BelowThresholdBehaviour);
                    return true;

                case ResolvedNodeKind.All:
                {
                    var firstSelectedIndex = selectedCount;
                    var soundedAnything = false;
                    for (var childOrdinal = 0; childOrdinal < node.ChildCount; childOrdinal++)
                        soundedAnything |= SelectNode(
                            resolvedEvent,
                            resolvedEvent.ChildNodeIndices[node.FirstChildIndex + childOrdinal],
                            destination,
                            ref selectedCount,
                            ref droppedSoundCount);
                    var fadeInFrames = MillisecondsToFrames(node.FadeInMilliseconds);
                    var fadeOutFrames = MillisecondsToFrames(node.FadeOutMilliseconds);
                    for (var selectedIndex = firstSelectedIndex; selectedIndex < selectedCount; selectedIndex++)
                    {
                        destination[selectedIndex] = destination[selectedIndex] with
                        {
                            ContainerFadeInFrames = Math.Max(destination[selectedIndex].ContainerFadeInFrames, fadeInFrames),
                            ContainerFadeOutFrames = Math.Max(destination[selectedIndex].ContainerFadeOutFrames, fadeOutFrames)
                        };
                    }
                    return soundedAnything;
                }

                case ResolvedNodeKind.Sequence:
                    if (node.AlwaysResetsPlaylist)
                        node.InstanceState.SequenceIndex = 0;
                    if (node.IsContinuous)
                        return SelectContinuous(resolvedEvent, nodeIndex, ref node, destination, ref selectedCount, ref droppedSoundCount);
                    return SelectFromPlaylist(resolvedEvent, ref node, node.InstanceState.SequenceIndex, destination, ref selectedCount, ref droppedSoundCount);

                case ResolvedNodeKind.Random:
                    if (node.IsContinuous)
                        return SelectContinuous(resolvedEvent, nodeIndex, ref node, destination, ref selectedCount, ref droppedSoundCount);
                    return SelectFromPlaylist(resolvedEvent, ref node, WeightedPick(resolvedEvent, ref node), destination, ref selectedCount, ref droppedSoundCount);

                default:
                    return false;
            }
        }

        private static bool SelectContinuous(
            ResolvedEvent resolvedEvent,
            int nodeIndex,
            ref ResolvedNode node,
            SelectedSound[] destination,
            ref int selectedCount,
            ref int droppedSoundCount)
        {
            var soundedAnything = false;
            var delayFrames = 0;
            var firstContinuousSelectedIndex = selectedCount;
            var loopCount = node.LoopCount == 0 ? 1 : Math.Max(1, node.LoopCount);
            if (node.LoopMaximumOffset > 0)
            {
                var offsetRange = Math.Max(0, node.LoopMaximumOffset - node.LoopMinimumOffset);
                loopCount += node.LoopMinimumOffset + node.InstanceState.NextRandom(offsetRange + 1);
            }
            var entriesPerLoop = node.Kind == ResolvedNodeKind.Sequence ? node.ChildCount : 1;
            var previousFirstSelectedIndex = -1;
            var previousSelectedCount = 0;
            for (var loop = 0; loop < loopCount && selectedCount < destination.Length; loop++)
            {
                for (var entry = 0; entry < entriesPerLoop && selectedCount < destination.Length; entry++)
                {
                    var firstSelectedIndex = selectedCount;
                    var selected = node.Kind == ResolvedNodeKind.Sequence
                        ? SelectFromPlaylist(resolvedEvent, ref node, node.InstanceState.SequenceIndex, destination, ref selectedCount, ref droppedSoundCount)
                        : SelectFromPlaylist(resolvedEvent, ref node, WeightedPick(resolvedEvent, ref node), destination, ref selectedCount, ref droppedSoundCount);
                    if (!selected)
                        continue;

                    soundedAnything = true;
                    var longestDurationFrames = 0;
                    var transitionFrames = TransitionFrames(ref node);
                    var isCrossfade = node.TransitionMode is AkTransitionMode.CrossFadeAmp or AkTransitionMode.CrossFadePower;
                    if (isCrossfade && previousFirstSelectedIndex >= 0)
                    {
                        for (var previousIndex = previousFirstSelectedIndex; previousIndex < previousSelectedCount; previousIndex++)
                        {
                            destination[previousIndex] = destination[previousIndex] with
                            {
                                ContainerEndFadeOutFrames = transitionFrames,
                                UsesEqualPowerCrossfade = node.TransitionMode == AkTransitionMode.CrossFadePower
                            };
                        }
                    }
                    for (var selectedIndex = firstSelectedIndex; selectedIndex < selectedCount; selectedIndex++)
                    {
                        destination[selectedIndex] = destination[selectedIndex] with
                        {
                            ContainerDelayFrames = destination[selectedIndex].ContainerDelayFrames + delayFrames,
                            ContainerFadeInFrames = node.TransitionMode is AkTransitionMode.CrossFadeAmp or AkTransitionMode.CrossFadePower
                                ? transitionFrames
                                : destination[selectedIndex].ContainerFadeInFrames,
                            UsesEqualPowerCrossfade = node.TransitionMode == AkTransitionMode.CrossFadePower
                        };
                        longestDurationFrames = Math.Max(
                            longestDurationFrames,
                            ToIntFrames(destination[selectedIndex].Media.Duration));
                    }
                    previousFirstSelectedIndex = firstSelectedIndex;
                    previousSelectedCount = selectedCount;

                    delayFrames += node.TransitionMode switch
                    {
                        AkTransitionMode.Delay => longestDurationFrames + transitionFrames,
                        AkTransitionMode.TriggerRate => transitionFrames,
                        AkTransitionMode.CrossFadeAmp or AkTransitionMode.CrossFadePower => Math.Max(0, longestDurationFrames - transitionFrames),
                        _ => longestDurationFrames
                    };
                }
            }
            if (node.LoopCount == 0 && soundedAnything)
            {
                for (var selectedIndex = firstContinuousSelectedIndex; selectedIndex < selectedCount; selectedIndex++)
                {
                    destination[selectedIndex] = destination[selectedIndex] with
                    {
                        ContinuationNodeIndex = nodeIndex,
                        ContinuationFrames = Math.Max(1, delayFrames)
                    };
                }
            }
            return soundedAnything;
        }

        private static int TransitionFrames(ref ResolvedNode node)
        {
            var milliseconds = node.TransitionMilliseconds.Value;
            if (node.TransitionMilliseconds.IsRandomised)
            {
                var range = node.TransitionMilliseconds.Maximum - node.TransitionMilliseconds.Minimum;
                milliseconds += node.TransitionMilliseconds.Minimum
                    + range * node.InstanceState.NextRandom(1_000_001) / 1_000_000f;
            }
            return ToIntFrames(TimeSpan.FromMilliseconds(Math.Max(0f, milliseconds)));
        }

        private static int ToIntFrames(TimeSpan duration)
            => (int)Math.Min(int.MaxValue, PlaybackTime.ToFrames(duration));

        private static int MillisecondsToFrames(float milliseconds)
            => ToIntFrames(TimeSpan.FromMilliseconds(Math.Max(0f, milliseconds)));

        // Starts at the chosen entry and walks the rest of the playlist behind it, so a variation
        // that cannot sound costs a variation rather than the whole cue.
        private static bool SelectFromPlaylist(
            ResolvedEvent resolvedEvent,
            ref ResolvedNode node,
            int firstChildOrdinal,
            SelectedSound[] destination,
            ref int selectedCount,
            ref int droppedSoundCount)
        {
            if (node.ChildCount == 0)
                return false;

            for (var attempt = 0; attempt < node.ChildCount; attempt++)
            {
                var childOrdinal = (firstChildOrdinal + attempt) % node.ChildCount;
                if (node.IsShuffle && !node.InstanceState.IsShuffleEntryAvailable(childOrdinal))
                    continue;
                var childNodeIndex = resolvedEvent.ChildNodeIndices[node.FirstChildIndex + childOrdinal];
                if (!SelectNode(resolvedEvent, childNodeIndex, destination, ref selectedCount, ref droppedSoundCount))
                    continue;

                node.InstanceState.SequenceIndex = (childOrdinal + 1) % node.ChildCount;
                node.InstanceState.RecordPlayed(resolvedEvent.Nodes[childNodeIndex].NodeId);
                if (node.IsShuffle)
                    node.InstanceState.RecordShuffleEntry(childOrdinal);
                return true;
            }
            return false;
        }

        private static int WeightedPick(ResolvedEvent resolvedEvent, ref ResolvedNode node)
        {
            if (node.IsShuffle)
                node.InstanceState.ResetShuffleBag();

            // Entries lately played are passed over — unless that leaves nothing, which is what
            // happens on the pass after the history has filled up with the whole playlist.
            var eligibleWeight = EligibleWeight(resolvedEvent, ref node, honourHistory: true);
            var honourHistory = eligibleWeight > 0;
            if (!honourHistory)
                eligibleWeight = EligibleWeight(resolvedEvent, ref node, honourHistory: false);
            if (eligibleWeight <= 0)
                return 0;

            var remainingWeight = node.InstanceState.NextRandom(eligibleWeight);
            for (var childOrdinal = 0; childOrdinal < node.ChildCount; childOrdinal++)
            {
                if (!node.InstanceState.IsShuffleEntryAvailable(childOrdinal)
                    || honourHistory && WasRecentlyPlayed(resolvedEvent, ref node, childOrdinal))
                    continue;

                remainingWeight -= PlaylistWeight(resolvedEvent, ref node, childOrdinal);
                if (remainingWeight < 0)
                    return childOrdinal;
            }
            return 0;
        }

        private static int EligibleWeight(ResolvedEvent resolvedEvent, ref ResolvedNode node, bool honourHistory)
        {
            var eligibleWeight = 0;
            for (var childOrdinal = 0; childOrdinal < node.ChildCount; childOrdinal++)
            {
                if (!node.InstanceState.IsShuffleEntryAvailable(childOrdinal)
                    || honourHistory && WasRecentlyPlayed(resolvedEvent, ref node, childOrdinal))
                    continue;
                eligibleWeight += PlaylistWeight(resolvedEvent, ref node, childOrdinal);
            }
            return eligibleWeight;
        }

        private static bool WasRecentlyPlayed(ResolvedEvent resolvedEvent, ref ResolvedNode node, int childOrdinal)
            => node.InstanceState.WasRecentlyPlayed(
                resolvedEvent.Nodes[resolvedEvent.ChildNodeIndices[node.FirstChildIndex + childOrdinal]].NodeId);

        // At least one, so an entry authored with no weight is still reachable.
        private static int PlaylistWeight(ResolvedEvent resolvedEvent, ref ResolvedNode node, int childOrdinal)
            => Math.Max(1, resolvedEvent.ChildWeights[node.FirstChildIndex + childOrdinal]);
    }
}
