namespace Shared.GameFormats.Wwise.Hirc
{
    // The authored properties a sound engine reads, named independently of the bank version. V112 and
    // V136 number their property ids differently, so each version maps its own numbering onto these.
    public enum WwiseProperty
    {
        Volume,
        BusVolume,
        MakeUpGain,
        OutputBusVolume,
        Pitch,
        LowPassFilter,
        HighPassFilter,
        Priority,
        PriorityDistanceOffset,
        InitialDelay,
        ActionDelay,
        TransitionTime,
        Probability,
        LoopCount,
        CentrePercent,
        PanLeftRight,
        PanFrontRear,
        GameAuxSendVolume,
        UserAuxSendVolume0,
        UserAuxSendVolume1,
        UserAuxSendVolume2,
        UserAuxSendVolume3,
        AttenuationId
    }

    // What a property's 32 stored bits mean. A bank stores every property in four bytes and says
    // nothing about how to read them; the property id is the only discriminator there is.
    public enum WwisePropertyStorage
    {
        Number,
        Milliseconds,
        Count,
        Identifier
    }

    // One authored property as the bank stores it, with the reading its id licenses. Decoding is
    // deliberately not done at parse time: the stored bits are what the file says, and a wrong
    // reading should be visible as a wrong accessor rather than as a silently converted value.
    public readonly record struct AuthoredPropertyValue(WwiseProperty Property, uint StoredBits)
    {
        public WwisePropertyStorage Storage => AuthoredProperties.GetStorage(Property);

        // Decibels, cents, percent or seconds depending on the property, always IEEE-754 as stored.
        public float Number => Storage == WwisePropertyStorage.Number
            ? BitConverter.UInt32BitsToSingle(StoredBits)
            : throw new InvalidOperationException($"{Property} is stored as {Storage}, not a number.");

        // Signed: an action's delay range reaches below zero, and its minimum normally does.
        public int Milliseconds => Storage == WwisePropertyStorage.Milliseconds
            ? unchecked((int)StoredBits)
            : throw new InvalidOperationException($"{Property} is stored as {Storage}, not a time in milliseconds.");

        public int Count => Storage == WwisePropertyStorage.Count
            ? unchecked((int)StoredBits)
            : throw new InvalidOperationException($"{Property} is stored as {Storage}, not a count.");

        public uint Identifier => Storage == WwisePropertyStorage.Identifier
            ? StoredBits
            : throw new InvalidOperationException($"{Property} is stored as {Storage}, not an identifier.");
    }

    // The properties one HIRC object authors: a value per property, and for some of them the range
    // the value is randomised within. A property the object does not author is absent rather than
    // neutral, because whether a value was authored at all is what decides if it overrides a parent.
    public sealed class AuthoredProperties
    {
        public static AuthoredProperties None { get; } = new([], []);

        private readonly AuthoredPropertyValue[] _values;
        private readonly (AuthoredPropertyValue Minimum, AuthoredPropertyValue Maximum)[] _ranges;

        public AuthoredProperties(
            IEnumerable<AuthoredPropertyValue> values,
            IEnumerable<(AuthoredPropertyValue Minimum, AuthoredPropertyValue Maximum)> ranges)
        {
            _values = values.ToArray();
            _ranges = ranges.ToArray();
        }

        public IReadOnlyList<AuthoredPropertyValue> Values => _values;

        public bool TryGetValue(WwiseProperty property, out AuthoredPropertyValue value)
        {
            foreach (var candidate in _values)
            {
                if (candidate.Property != property)
                    continue;

                value = candidate;
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetRange(WwiseProperty property, out AuthoredPropertyValue minimum, out AuthoredPropertyValue maximum)
        {
            foreach (var (candidateMinimum, candidateMaximum) in _ranges)
            {
                if (candidateMinimum.Property != property)
                    continue;

                minimum = candidateMinimum;
                maximum = candidateMaximum;
                return true;
            }

            minimum = default;
            maximum = default;
            return false;
        }

        // Settled on 2026-08-30 against every HIRC object in the 244 banks of Warhammer III's
        // audio_base_bnk.pack: mixing properties hold IEEE-754 floats, an action's delay and
        // transition hold signed millisecond integers, and an attenuation reference holds an id.
        // Recomputing each object's size from the model and comparing it with the size the bank
        // states is what proves the fields were read where the format actually puts them.
        public static WwisePropertyStorage GetStorage(WwiseProperty property) => property switch
        {
            WwiseProperty.ActionDelay or WwiseProperty.TransitionTime => WwisePropertyStorage.Milliseconds,
            WwiseProperty.LoopCount => WwisePropertyStorage.Count,
            WwiseProperty.AttenuationId => WwisePropertyStorage.Identifier,
            _ => WwisePropertyStorage.Number
        };
    }
}
