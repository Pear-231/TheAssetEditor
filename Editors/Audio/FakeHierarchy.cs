using Editors.Audio.Shared.Wwise.Engine.Hierarchy;
using Editors.Audio.Shared.Wwise.Engine.Media;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;
using static Shared.GameFormats.Wwise.Hirc.V136.CAkRanSeqCntr_V136.CAkPlayList_V136;

namespace Test.Audio
{
    // A bank built by hand, so the walk can be measured against a hierarchy whose shape is stated
    // in the test rather than found in a pack file.
    //
    // The nodes are the real parsed types: what is faked is where they came from, not what they
    // are, so a test that says "a random container of three sounds" is exercising the same
    // properties a bank would have given the walker.
    internal sealed class FakeHierarchy : IHierarchyProvider
    {
        private const string BnkFilePath = "test.bnk";

        private readonly Dictionary<uint, HircItem> _nodesById = [];
        private readonly Dictionary<string, HircItem> _eventsByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, SourceMedia> _mediaBySourceId = [];
        private readonly Dictionary<uint, string> _namesById = [];
        private readonly Dictionary<string, uint> _idsByName = new(StringComparer.OrdinalIgnoreCase);
        private uint _nextGeneratedId = 100_000;
        private HircItem? _lastAddedNode;

        public HircItem? FindEvent(string eventName) => _eventsByName.GetValueOrDefault(eventName);

        public HircItem? FindNode(uint nodeId, string referringBnkFilePath) => _nodesById.GetValueOrDefault(nodeId);

        public string GetName(uint id) => _namesById.TryGetValue(id, out var name) ? name : id.ToString();

        public SourceMedia? FindMedia(uint sourceId, string referringBnkFilePath) => _mediaBySourceId.GetValueOrDefault(sourceId);

        public IReadOnlyList<HircItem> GetMasterMixerNodes()
            => _nodesById.Values.Where(node => node is ICAkBus).ToArray();

        // One Play action per target, which is how an event that starts several things is authored.
        public FakeHierarchy WithEvent(string eventName, params uint[] targetNodeIds)
            => WithActionEvent(eventName, [.. targetNodeIds.Select(targetNodeId => (AkActionType.Play, targetNodeId, 0, 0))]);

        // An event whose actions are spelled out: what each one does, what it targets, and the delay
        // and fade it authors in milliseconds.
        public FakeHierarchy WithActionEvent(
            string eventName,
            params (AkActionType ActionType, uint TargetNodeId, int DelayMilliseconds, int FadeMilliseconds)[] actions)
        {
            var actionEvent = new CAkEvent_V136 { Id = ++_nextGeneratedId, BnkFilePath = BnkFilePath };
            foreach (var (actionType, targetNodeId, delayMilliseconds, fadeMilliseconds) in actions)
            {
                var actionId = ++_nextGeneratedId;
                var action = new CAkAction_V136
                {
                    Id = actionId,
                    BnkFilePath = BnkFilePath,
                    ActionType = actionType,
                    IdExt = targetNodeId
                };
                if (delayMilliseconds != 0)
                    action.AkPropBundle0.PropsList.Add(new AkPropBundle_V136.PropBundleInstance_V136
                    {
                        Id = AkPropId_V136.DelayTime,
                        Value = unchecked((uint)delayMilliseconds)
                    });
                if (fadeMilliseconds != 0)
                    action.AkPropBundle0.PropsList.Add(new AkPropBundle_V136.PropBundleInstance_V136
                    {
                        Id = AkPropId_V136.TransitionTime,
                        Value = unchecked((uint)fadeMilliseconds)
                    });

                Add(action);
                actionEvent.Actions.Add(new CAkEvent_V136.Action_V136 { ActionId = actionId });
            }

            // Indexed by id as well as by name, because an event is a node like any other and a
            // caller that picked one out of the hierarchy holds its id rather than its name.
            _eventsByName.Add(eventName, actionEvent);
            _namesById[actionEvent.Id] = eventName;
            return Add(actionEvent);
        }

        // A null media stands for a bank naming a source that is not there, which is what a missing
        // variation looks like from the walker's side.
        public FakeHierarchy WithSound(uint nodeId, SourceMedia? media)
        {
            var sourceId = nodeId + 1_000_000;
            Add(new CAkSound_V136
            {
                Id = nodeId,
                BnkFilePath = BnkFilePath,
                AkBankSourceData = new AkBankSourceData_V136
                {
                    AkMediaInformation = new AkBankSourceData_V136.AkMediaInformation_V136 { SourceId = sourceId }
                }
            });
            if (media != null)
                _mediaBySourceId[sourceId] = media;
            _namesById[nodeId] = $"sound-{nodeId}";
            return this;
        }

        // Applies to the node just added, which is where Wwise puts it: an instance limit is a
        // property of a node and governs everything beneath it, so a container is the usual place
        // for it rather than a sound.
        //
        public FakeHierarchy LimitedTo(ushort maximumInstanceCount)
        {
            var advancedSettings = NodeBaseParamsOf(_lastAddedNode!).AdvSettingsParams;
            advancedSettings.MaxNumInstance = maximumInstanceCount;
            return this;
        }

        public FakeHierarchy WithGlobalLimit()
        {
            NodeBaseParamsOf(_lastAddedNode!).AdvSettingsParams.BitVector |= 0x04;
            return this;
        }

        public FakeHierarchy DiscardNewestOnLimit()
        {
            NodeBaseParamsOf(_lastAddedNode!).AdvSettingsParams.BitVector |= 0x01;
            return this;
        }

        public FakeHierarchy VirtualiseOnLimit()
        {
            NodeBaseParamsOf(_lastAddedNode!).AdvSettingsParams.BitVector |= 0x02;
            return this;
        }

        // What a node has to carry for its own priority to count rather than its parent's.
        private const byte PriorityOverridesParentBit = 0x01;

        // Authored on the node just added, as a bank stores it: a value, and optionally the range
        // the instance is randomised within.
        public FakeHierarchy WithProperty(WwiseProperty property, float value, float minimum = 0f, float maximum = 0f, bool overridesParent = true)
        {
            var propertyId = PropertyIdOf(property);
            var bundles = NodeBaseParamsOf(_lastAddedNode!).NodeInitialParams;
            bundles.AkPropBundle0.PropsList.Add(new AkPropBundle_V136.PropBundleInstance_V136
            {
                Id = propertyId,
                Value = BitConverter.SingleToUInt32Bits(value)
            });

            if (maximum > minimum)
            {
                bundles.AkPropBundle1.PropsList.Add(new AkPropBundleMinMax_V136.AkPropBundleInstance_V136
                {
                    Type = propertyId,
                    Min = BitConverter.SingleToUInt32Bits(minimum),
                    Max = BitConverter.SingleToUInt32Bits(maximum)
                });
            }

            // Priority is absolute, so a node that states one has to say it is overriding what it
            // inherited, exactly as a bank does. A bank can also store one without that bit — Wwise
            // keeps the last value the author typed — and then the value is not applied at all.
            if (property == WwiseProperty.Priority && overridesParent)
                NodeBaseParamsOf(_lastAddedNode!).BitVector |= PriorityOverridesParentBit;

            return this;
        }

        // What a SetSwitch action in the named event selects. Applied to every SetSwitch action of
        // that event, which is what an event with one of them looks like.
        public FakeHierarchy WithSwitchOnAction(string eventName, string switchGroupName, string switchValueName)
        {
            var actionEvent = (CAkEvent_V136)_eventsByName[eventName];
            foreach (var eventAction in actionEvent.Actions)
            {
                if (_nodesById[eventAction.ActionId] is not CAkAction_V136 action || action.ActionType != AkActionType.SetSwitch)
                    continue;

                action.SwitchActionParams = new CAkAction_V136.SwitchActionParams_V136
                {
                    SwitchGroupId = NameId(switchGroupName),
                    SwitchValueId = NameId(switchValueName)
                };
            }
            return this;
        }

        // Who the node just added hangs under, which is where its volume and priority come from when
        // the walk enters the hierarchy below that point.
        public FakeHierarchy Under(uint parentNodeId)
        {
            NodeBaseParamsOf(_lastAddedNode!).DirectParentId = parentNodeId;
            return this;
        }

        private static AkPropId_V136 PropertyIdOf(WwiseProperty property) => property switch
        {
            WwiseProperty.Volume => AkPropId_V136.Volume,
            WwiseProperty.MakeUpGain => AkPropId_V136.MakeUpGain,
            WwiseProperty.Pitch => AkPropId_V136.Pitch,
            WwiseProperty.LowPassFilter => AkPropId_V136.LPF,
            WwiseProperty.HighPassFilter => AkPropId_V136.HPF,
            WwiseProperty.Priority => AkPropId_V136.Priority,
            _ => throw new ArgumentException($"{property} is not one a test authors.", nameof(property))
        };

        public FakeHierarchy WithRandomContainer(uint nodeId, ushort avoidRepeatCount, params uint[] childIds)
            => WithRandomSequenceContainer(nodeId, AkContainerMode.Random, avoidRepeatCount, childIds);

        public FakeHierarchy WithSequenceContainer(uint nodeId, params uint[] childIds)
            => WithRandomSequenceContainer(nodeId, AkContainerMode.Sequence, avoidRepeatCount: 0, childIds);

        public FakeHierarchy AsShuffle()
        {
            ((CAkRanSeqCntr_V136)_lastAddedNode!).RandomMode = (byte)AkRandomMode.Shuffle;
            return this;
        }

        public FakeHierarchy WithGlobalScope()
        {
            ((CAkRanSeqCntr_V136)_lastAddedNode!).BitVector |= 0x10;
            return this;
        }

        public FakeHierarchy AlwaysResetPlaylist()
        {
            ((CAkRanSeqCntr_V136)_lastAddedNode!).BitVector |= 0x02;
            return this;
        }

        public FakeHierarchy AsContinuous(ushort loopCount = 1)
        {
            var container = (CAkRanSeqCntr_V136)_lastAddedNode!;
            container.BitVector |= 0x08;
            container.LoopCount = loopCount;
            return this;
        }

        public FakeHierarchy WithLoopRandomisation(ushort minimumOffset, ushort maximumOffset)
        {
            var container = (CAkRanSeqCntr_V136)_lastAddedNode!;
            container.LoopModMin = minimumOffset;
            container.LoopModMax = maximumOffset;
            return this;
        }

        public FakeHierarchy WithTransition(AkTransitionMode mode, float milliseconds)
        {
            var container = (CAkRanSeqCntr_V136)_lastAddedNode!;
            container.TransitionMode = (byte)mode;
            container.TransitionTime = milliseconds;
            return this;
        }

        public FakeHierarchy WithLayerContainer(uint nodeId, params uint[] childIds)
        {
            var layerContainer = new CAkLayerCntr_V136 { Id = nodeId, BnkFilePath = BnkFilePath };
            layerContainer.Children.ChildIds.AddRange(childIds);
            return Add(layerContainer);
        }

        public FakeHierarchy WithLayerCurve(
            string gameParameterName,
            uint childId,
            params (float Input, float GainDecibels, uint Interpolation)[] points)
        {
            var layerContainer = (CAkLayerCntr_V136)_lastAddedNode!;
            var layer = new CAkLayerCntr_V136.CAkLayer_V136
            {
                RtpcId = NameId(gameParameterName),
                RtpcType = AkRtpcType.GameParameter
            };
            var child = new CAkLayerCntr_V136.CAssociatedChildData_V136 { AssociatedChildId = childId };
            foreach (var (input, gainDecibels, interpolation) in points)
            {
                child.AkRtpcGraphPointList.Add(new AkRtpcGraphPoint_V136
                {
                    From = input,
                    To = gainDecibels,
                    Interp = interpolation
                });
            }
            layer.CAssociatedChildDataList.Add(child);
            layerContainer.LayerList.Add(layer);
            return this;
        }

        public FakeHierarchy WithContinuousLayerValidation()
        {
            ((CAkLayerCntr_V136)_lastAddedNode!).IsContinuousValidation = 1;
            return this;
        }

        public FakeHierarchy WithActorMixer(uint nodeId, params uint[] childIds)
        {
            var actorMixer = new CAkActorMixer_V136 { Id = nodeId, BnkFilePath = BnkFilePath };
            actorMixer.Children.ChildIds.AddRange(childIds);
            return Add(actorMixer);
        }

        public FakeHierarchy WithSwitchContainer(
            uint nodeId,
            string switchGroupName,
            string defaultSwitchValueName,
            params (string SwitchValueName, uint[] ChildIds)[] branches)
            => WithGroupContainer(nodeId, AkGroupType.Switch, switchGroupName, defaultSwitchValueName, branches);

        public FakeHierarchy WithContinuousSwitchValidation(
            int fadeOutMilliseconds,
            int fadeInMilliseconds,
            params uint[] childIds)
        {
            var container = (CAkSwitchCntr_V136)_lastAddedNode!;
            container.BIsContinuousValidation = 1;
            foreach (var childId in childIds)
            {
                container.Parameters.Add(new CAkSwitchCntr_V136.AkSwitchNodeParams_V136
                {
                    NodeId = childId,
                    FadeOutTime = fadeOutMilliseconds,
                    FadeInTime = fadeInMilliseconds
                });
            }
            return this;
        }

        // The same container switching on a state group, which is the game's value rather than the
        // emitter's.
        public FakeHierarchy WithStateContainer(
            uint nodeId,
            string stateGroupName,
            string defaultStateValueName,
            params (string StateValueName, uint[] ChildIds)[] branches)
            => WithGroupContainer(nodeId, AkGroupType.State, stateGroupName, defaultStateValueName, branches);

        private FakeHierarchy WithGroupContainer(
            uint nodeId,
            AkGroupType groupType,
            string switchGroupName,
            string defaultSwitchValueName,
            (string SwitchValueName, uint[] ChildIds)[] branches)
        {
            var switchContainer = new CAkSwitchCntr_V136
            {
                Id = nodeId,
                BnkFilePath = BnkFilePath,
                EGroupType = groupType,
                GroupId = NameId(switchGroupName)
            };
            foreach (var (switchValueName, childIds) in branches)
            {
                var switchPackage = new CAkSwitchCntr_V136.CAkSwitchPackage_V136 { SwitchId = NameId(switchValueName) };
                switchPackage.NodeIdList.AddRange(childIds);
                switchContainer.SwitchList.Add(switchPackage);
            }
            switchContainer.DefaultSwitch = NameId(defaultSwitchValueName);
            return Add(switchContainer);
        }

        private FakeHierarchy WithRandomSequenceContainer(
            uint nodeId,
            AkContainerMode containerMode,
            ushort avoidRepeatCount,
            uint[] childIds)
        {
            var randomSequenceContainer = new CAkRanSeqCntr_V136
            {
                Id = nodeId,
                BnkFilePath = BnkFilePath,
                Mode = (byte)containerMode,
                AvoidRepeatCount = avoidRepeatCount
            };
            foreach (var childId in childIds)
                randomSequenceContainer.CAkPlayList.Playlist.Add(new AkPlaylistItem_V136 { PlayId = childId, Weight = 50_000 });
            return Add(randomSequenceContainer);
        }

        // One id per name, so a branch and the default that names it resolve to the same switch.
        private uint NameId(string name)
        {
            if (_idsByName.TryGetValue(name, out var existingId))
                return existingId;

            var id = ++_nextGeneratedId;
            _idsByName.Add(name, id);
            _namesById[id] = name;
            return id;
        }

        private static NodeBaseParams_V136 NodeBaseParamsOf(HircItem hircItem) => hircItem switch
        {
            CAkSound_V136 sound => sound.NodeBaseParams,
            CAkRanSeqCntr_V136 randomSequenceContainer => randomSequenceContainer.NodeBaseParams,
            CAkLayerCntr_V136 layerContainer => layerContainer.NodeBaseParams,
            CAkSwitchCntr_V136 switchContainer => switchContainer.NodeBaseParams,
            CAkActorMixer_V136 actorMixer => actorMixer.NodeBaseParams,
            _ => throw new ArgumentException($"A {hircItem.GetType().Name} carries no node parameters to limit.", nameof(hircItem))
        };

        private FakeHierarchy Add(HircItem hircItem)
        {
            _nodesById[hircItem.Id] = hircItem;
            _lastAddedNode = hircItem;
            return this;
        }
    }
}
