namespace Editors.Audio.Shared.Wwise.Engine.Parameters
{
    // Produces the parameters for one sound instance.
    //
    // It runs at cue time, on the audio thread, which is deliberate: volume randomisation, RTPC
    // values and attenuation are properties of the instance rather than of the node, so resolving
    // them at warm time would mean every pass of a looping animation sounded identically. It is
    // real-time safe because it returns a value type and reads nothing that is written while it
    // runs — and it must stay that way, which means anything it needs from the HIRC tree has to be
    // computed onto the resolved node at warm time rather than looked up from here.
    //
    // One concrete resolver rather than a chain of contributors, until there are two real
    // contributors to justify the interface.
    internal sealed class ParameterResolver
    {
        // Its own generator rather than the container's: a sound reached without any container above
        // it still randomises, and the sequence cursor must not be disturbed by drawing for volume.
        private uint _randomState = 0x9E3779B9u;

        // What the game object carries is deliberately not an argument. Nothing the engine plays is
        // driven by a game parameter yet — that needs the RTPC curves, which are parsed but not
        // evaluated — and when it is, the value will arrive the way everything else the audio thread
        // reads arrives: attached to the plan at warm time, not looked up from in here.
        public VoiceParameters Resolve(in AuthoredParameters authored)
        {
            return new VoiceParameters(
                Volume: AudioLevel.DecibelsToLinear(Randomise(authored.VolumeDecibels)),
                PitchCents: Randomise(authored.PitchCents),
                LowPassFilter: Math.Clamp(Randomise(authored.LowPassFilter), 0f, 100f),
                HighPassFilter: Math.Clamp(Randomise(authored.HighPassFilter), 0f, 100f),
                Priority: Math.Clamp(authored.Priority, 0f, 100f),
                OutputBusId: authored.OutputBusId);
        }

        // Direct media has no hierarchy above it and nothing to randomise, but it still goes through
        // here so the one place that decides what a voice is told about itself stays one place.
        public VoiceParameters ResolveDirectMedia() => VoiceParameters.Neutral;

        // An action's delay and fade are randomised the same way a volume is, and by the same
        // generator, so that two cues from one action do not land in lockstep.
        public float Resolve(in RandomisedProperty property) => Randomise(property);

        private float Randomise(in RandomisedProperty property)
            => property.IsRandomised
                ? property.Value + property.Minimum + (property.Maximum - property.Minimum) * NextRandomFraction()
                : property.Value;

        // Xorshift rather than Random, because this runs on the audio thread where Random.Shared is
        // neither allocation free in every path nor reproducible between runs.
        private float NextRandomFraction()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState >> 8) / (float)(1 << 24);
        }
    }
}
