using Editors.Audio.Shared.Wwise.Engine.Media;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Behaviour;

namespace Editors.Audio.Shared.Wwise.Engine.Mixing
{
    // Everything a voice is started with, in one value.
    //
    // It is a parameter object rather than a dozen arguments because the list grew past the point
    // where a call site could be read: what a voice needs to know is media, what it sounds like, who
    // it belongs to, where it sits in the hierarchy, and when it starts. Those are five ideas, not
    // thirteen positions.
    internal readonly record struct VoiceStart(
        PlayingId PlayingId,
        PostCompletionState PostCompletion,
        SourceMedia Media,
        VoiceParameters Parameters,
        VoiceLimit Limit,
        ReadOnlyMemory<VoiceLimit> Limits,

        // Which sound this is and who it is playing for, so an action that names a node can find the
        // voices under it. The ancestors are the path warming walked, which is the only way the audio
        // thread can answer "is this playing underneath that container" without the hierarchy.
        uint NodeId,
        GameObjectId GameObject,
        ReadOnlyMemory<uint> AncestorNodeIds,

        long InitialMixFrame,

        // What an action authored: how long before this voice starts, and how long it takes to reach
        // full gain once it does. Both are zero for a sound that simply plays.
        int StartDelayFrames,
        int FadeInFrames,

        bool IsLooping,
        bool IsScheduledVoice,
        int RetargetFadeOutFrames = 0,
        int EndFadeOutFrames = 0,
        bool UsesEqualPowerCrossfade = false,
        int LoopCount = 1,
        AttenuationSettings Attenuation = null,
        GameObjectParameterSource GameObjectParameters = null,
        bool IsPositioned = false,
        byte VirtualQueueBehaviour = 2,
        byte BelowThresholdBehaviour = 0,
        bool ForceFadeIn = false,

        // How far the read head is nudged off the requested position so the waveform continues
        // rather than restarting. Separate from InitialMixFrame on purpose: the position the
        // caller asked for is the position that gets reported, and only what is read moves.
        double SourceFrameAlignment = 0d)
    {
        // Direct media and the tests that stand in for it: no hierarchy, no action, no delay.
        public static VoiceStart ForMedia(
            PlayingId playingId,
            PostCompletionState postCompletion,
            SourceMedia media,
            VoiceParameters parameters,
            long initialMixFrame,
            bool isLooping)
            => new(
                playingId,
                postCompletion,
                media,
                parameters,
                VoiceLimit.None,
                ReadOnlyMemory<VoiceLimit>.Empty,
                NodeId: 0,
                GameObject: default,
                AncestorNodeIds: default,
                initialMixFrame,
                StartDelayFrames: 0,
                FadeInFrames: 0,
                isLooping,
                IsScheduledVoice: false,
                LoopCount: isLooping ? 0 : 1);
    }
}
