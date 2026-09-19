using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Wwise.Engine.Parameters
{
    // One authored value and the range the author allowed it to wander within.
    //
    // The range is kept rather than folded in because randomisation is a property of the instance,
    // not of the node: resolving it at warm time would make every pass of a looping animation
    // identical, which is the thing phase 4 removed.
    internal readonly record struct RandomisedProperty(float Value, float Minimum, float Maximum)
    {
        public static readonly RandomisedProperty None = new(0f, 0f, 0f);

        public bool IsRandomised => Maximum > Minimum;

        // Wwise sums a relative property down the actor-mixer hierarchy, and it sums the randomiser
        // ranges with it: a container that varies its children by ±2 dB and a sound that varies
        // itself by ±1 dB together vary by ±3 dB.
        public RandomisedProperty Add(in RandomisedProperty other)
            => new(Value + other.Value, Minimum + other.Minimum, Maximum + other.Maximum);
    }

    // What the hierarchy says about a sound, accumulated on the way down to it.
    //
    // Relative properties add: a sound's volume is its own plus every container above it. Absolute
    // properties do not — priority comes from the nearest node that said it overrides its parent's,
    // and is otherwise whatever was inherited from above.
    //
    // Built at warm time on the control thread and read at cue time on the audio thread, which is
    // why it is a value type carrying no references.
    internal readonly record struct AuthoredParameters(
        RandomisedProperty VolumeDecibels,
        RandomisedProperty PitchCents,
        RandomisedProperty LowPassFilter,
        RandomisedProperty HighPassFilter,
        float Priority,
        uint OutputBusId,
        int LoopCount,
        bool IsPositioned,
        uint AttenuationId,
        byte VirtualQueueBehaviour,
        byte BelowThresholdBehaviour)
    {
        // What a node inherits when nothing above it has said anything: unity gain, no pitch shift,
        // no filtering, and the middle of the priority range.
        public static readonly AuthoredParameters Inherited = new(
            RandomisedProperty.None,
            RandomisedProperty.None,
            RandomisedProperty.None,
            RandomisedProperty.None,
            VoiceParameters.DefaultPriority,
            OutputBusId: 0,
            LoopCount: 1,
            IsPositioned: false,
            AttenuationId: 0,
            VirtualQueueBehaviour: 2,
            BelowThresholdBehaviour: 0);

        public AuthoredParameters Accumulate(ICAkParameterNode parameterNode)
        {
            var properties = parameterNode.GetProperties();

            // Make-up gain is a second volume in decibels rather than a stage of its own: Wwise
            // applies it to the voice, and a voice has one gain.
            var volume = VolumeDecibels
                .Add(Read(properties, WwiseProperty.Volume))
                .Add(Read(properties, WwiseProperty.MakeUpGain));

            // Priority replaces rather than accumulates, and only where the node says it overrides
            // what it inherited. A node that stores a priority without that flag is carrying an
            // authored value Wwise itself does not apply.
            var priority = parameterNode.GetOverridesParentPriority()
                && properties.TryGetValue(WwiseProperty.Priority, out var authoredPriority)
                    ? authoredPriority.Number
                    : Priority;

            var outputBusId = parameterNode.GetOutputBusId();
            var loopCount = properties.TryGetValue(WwiseProperty.LoopCount, out var authoredLoopCount)
                ? authoredLoopCount.Count
                : LoopCount;
            var attenuationId = properties.TryGetValue(WwiseProperty.AttenuationId, out var authoredAttenuation)
                ? authoredAttenuation.Identifier
                : parameterNode.GetAttenuationId() != 0
                    ? parameterNode.GetAttenuationId()
                    : AttenuationId;
            return new AuthoredParameters(
                volume,
                PitchCents.Add(Read(properties, WwiseProperty.Pitch)),
                LowPassFilter.Add(Read(properties, WwiseProperty.LowPassFilter)),
                HighPassFilter.Add(Read(properties, WwiseProperty.HighPassFilter)),
                priority,
                outputBusId != 0 ? outputBusId : OutputBusId,
                loopCount,
                IsPositioned || parameterNode.GetIsPositioned(),
                attenuationId,
                parameterNode.GetVirtualQueueBehaviour(),
                parameterNode.GetBelowThresholdBehaviour());
        }


        public AuthoredParameters AddVolume(float decibels)
            => this with { VolumeDecibels = VolumeDecibels.Add(new RandomisedProperty(decibels, 0f, 0f)) };

        private static RandomisedProperty Read(AuthoredProperties properties, WwiseProperty property)
        {
            var value = properties.TryGetValue(property, out var authored) ? authored.Number : 0f;
            if (!properties.TryGetRange(property, out var minimum, out var maximum))
                return new RandomisedProperty(value, 0f, 0f);
            return new RandomisedProperty(value, minimum.Number, maximum.Number);
        }
    }
}
