using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;

namespace Shared.GameFormats.Wwise.Hirc
{
    public interface ICAkEvent
    {
        List<uint> GetActionIds();
    }

    // A state is a property override identified by its HIRC id. The engine does not apply these
    // properties yet; retaining the exact bundle keeps that later decision separate from parsing.
    public interface ICAkState
    {
        IReadOnlyList<IAkStateProperty> GetProperties();

        public interface IAkStateProperty
        {
            ushort PropertyId { get; }
            float Value { get; }
        }
    }

    public interface ICAkSound
    {
        uint GetDirectParentId();
        uint GetSourceId();
        AKBKSourceType GetStreamType();
    }

    public interface ICAkAction
    {
        AkActionType GetActionType();
        // The delay and transition an action authors, and the ranges it randomises them within.
        AuthoredProperties GetProperties();
        uint GetChildId();
        uint GetStateGroupId();

        // What a SetState action puts the group into, and what a SetSwitch action selects. Both are
        // zero on any other action type.
        uint GetTargetStateId();
        uint GetSwitchGroupId();
        uint GetSwitchValueId();
    }

    public interface ICAkDialogueEvent
    {
        List<IAkGameSync> Arguments { get; }
        IAkDecisionTree AkDecisionTree { get; }

        public interface IAkGameSync
        {
            uint GroupId { get; set;  }
            AkGroupType GroupType { get; set; }
            uint GetSize();
        }

        public interface IAkDecisionTree
        {
            void ReadData(ByteChunk chunk, uint treeDataSize, uint treeDepth);
            byte[] WriteData();
            IAkDecisionNode GetDecisionTree();
        }

        public interface IAkDecisionNode
        {
            uint GetKey();
            uint GetAudioNodeId();
            int GetChildrenCount();
            IAkDecisionNode GetChildAtIndex(int index);
        }
    }

    // What a node carries by virtue of sitting in the actor-mixer hierarchy at all, rather than by
    // being a sound or a particular kind of container. `NodeBaseParams` holds all of it on both
    // versions; only the instance limit is exposed so far, because it is the only part of it the
    // engine acts on. Bus routing, initial volumes and the state chunk hang off the same place.
    public interface ICAkParameterNode
    {
        // How many instances of this node may sound at once. Zero means no limit, which is what an
        // unauthored node carries and what this project's own bank generator writes.
        ushort GetMaxInstanceCount();

        // What the node authors of volume, pitch, filtering and priority. A property the node does
        // not author is absent rather than neutral, which is what lets a parent's value show through.
        AuthoredProperties GetProperties();

        // The node above this one in the actor-mixer hierarchy, which is where a property this node
        // does not author comes from. Zero at the top.
        uint GetDirectParentId();

        // Whether this node states its own priority rather than inheriting one. Priority is absolute:
        // it does not accumulate down the hierarchy the way volume and pitch do, so a node either
        // replaces its parent's or leaves it alone.
        bool GetOverridesParentPriority();

        // How this node's own limit is scoped and what happens when it is reached. These are only
        // meaningful when GetMaxInstanceCount is non-zero; an ancestor's limit remains an
        // independent budget rather than being replaced by a descendant's.
        bool GetIsGlobalLimit();
        bool GetDiscardsNewestOnLimit();
        bool GetUsesVirtualVoiceOnLimit();

        // The bus this node routes to, or zero to route wherever its parent does. The master-mixer
        // graph is built from these, so a node that names one is mixed through that bus's gain and
        // everything above it.
        uint GetOutputBusId();

        // Positioning and virtual-voice policy are inherited with the parameter node. Defaults keep
        // older bank versions explicit: they remain two-dimensional and continue below threshold.
        bool GetIsPositioned() => false;
        byte GetVirtualQueueBehaviour() => 2;
        byte GetBelowThresholdBehaviour() => 0;
        uint GetAttenuationId() => 0;
    }

    // Which distance curve a slot holds. Settled 2026-08-31 against campaign_vo__core.bnk's 876
    // attenuations: slots 0 to 2 are the dry and the two auxiliary volume curves and every one of
    // them is stored in decibel scaling, while slots 3 to 6 -- low-pass, high-pass, spread and
    // focus -- are percentages stored unscaled. That split is what identifies the slots: nothing
    // in the bank names them, and wwiser calls them only curveToUse[0..6].
    public enum AttenuationCurveType
    {
        Volume = 0,
        LowPassFilter = 3,
        HighPassFilter = 4
    }

    public interface ICAkAttenuation
    {
        WwiseCurve GetCurve(AttenuationCurveType curveType);
    }

    // A node in the master-mixer hierarchy. Effects and sends are exposed as identifiers rather
    // than decoded plug-ins: a replicated engine must be able to report what it cannot reproduce.
    public interface ICAkBus
    {
        uint GetOutputBusId();
        AuthoredProperties GetProperties();
        IReadOnlyList<uint> GetEffectIds();
        IReadOnlyList<uint> GetAuxiliaryBusIds();
    }

    public interface ICAkActorMixer
    {
        List<uint> GetChildren();
        uint GetDirectParentId();
    }

    public interface ICAkSwitchCntr
    {
        // Whether the group this container switches on is a switch group or a state group. The two
        // are set by different game-layer verbs and read from different places, so a container that
        // switches on a state cannot be resolved against switch values at all.
        AkGroupType GetGroupType();

        uint GroupId { get; }
        uint DefaultSwitch { get; }
        List<ICAkSwitchPackage> SwitchList { get; }

        bool GetIsContinuousValidation();
        IReadOnlyList<IAkSwitchNodeParams> GetNodeParameters();

        public interface ICAkSwitchPackage
        {
            uint SwitchId { get; }
            List<uint> NodeIdList { get; }
        }

        public interface IAkSwitchNodeParams
        {
            uint NodeId { get; }

            // Milliseconds, signed. Settled 2026-08-31: Warhammer III authors 1,290 non-zero fades
            // and Attila 94, all round values such as 1000 and 3000, which read as denormals if
            // taken as floats. Both are four bytes, so the section size agrees either way.
            int FadeOutTime { get; }
            int FadeInTime { get; }
        }

        uint GetDirectParentId();
    }

    public interface ICAkMusicTrack
    {
        List<uint> GetChildren();
    }

    public interface ICAkLayerCntr
    {
        List<uint> GetChildren();
        uint GetDirectParentId();
        bool GetIsContinuousValidation();
        IReadOnlyList<IAkLayer> GetLayers();

        public interface IAkLayer
        {
            uint RtpcId { get; }
            AkRtpcType RtpcType { get; }
            IReadOnlyList<IAkAssociatedChildData> GetAssociatedChildren();
        }

        public interface IAkAssociatedChildData
        {
            uint AssociatedChildId { get; }
            IReadOnlyList<IAkRtpcGraphPoint> GetCurvePoints();
        }

        public interface IAkRtpcGraphPoint
        {
            float From { get; }
            float To { get; }
            uint Interp { get; }
        }
    }

    // Exposes how the container chooses between its children, not only that it has them. The
    // parsers already read all of this on both versions; until now only the topology was reachable
    // through the version-agnostic interface, so a sound engine could see the playlist but not the
    // rules for playing it.
    public interface ICAkRanSeqCntr
    {
        uint GetDirectParentId();
        List<uint> GetChildren();
        AkContainerMode GetContainerMode();
        AkRandomMode GetRandomMode();
        AkTransitionMode GetTransitionMode();
        ushort GetAvoidRepeatCount();
        ushort GetLoopCount();
        ushort GetLoopMinimumOffset();
        ushort GetLoopMaximumOffset();
        float GetTransitionTime();
        float GetTransitionTimeMinimumOffset();
        float GetTransitionTimeMaximumOffset();
        bool GetIsContinuous();
        bool GetIsGlobal();
        bool GetAlwaysResetsPlaylist();
        IReadOnlyList<IAkPlaylistItem> GetPlaylist();

        // The playlist rather than the child list, because a weight only means anything in
        // playlist order.
        public interface IAkPlaylistItem
        {
            uint PlayId { get; }
            int Weight { get; }
        }
    }
}
