namespace Editors.Audio.Shared.Wwise.Engine.Parameters
{
    // What a voice is told about itself beyond which media it is playing.
    //
    // Every field holds its neutral default today, and that is the point rather than an omission:
    // HIRC volumes and pitch, per-instance randomisation, attenuation, RTPCs and states all have to
    // arrive as an answer to something, and until now there was nothing for them to be an answer to.
    //
    // A value type, because it is produced once per sound instance at cue time, on the audio thread.
    internal readonly record struct VoiceParameters(
        float Volume,
        float PitchCents,
        float LowPassFilter,
        float HighPassFilter,
        float Priority,
        uint OutputBusId)
    {
        // The middle of the range Wwise authors priority on. It decides which voice gives way when
        // the pool or a node instance limit is reached, and until every voice carries its own it
        // decides nothing: a newcomer has to *outrank* what is already sounding to take its place,
        // so with everything at the default nothing audible is ever cut.
        public const float DefaultPriority = 50f;

        public static readonly VoiceParameters Neutral = new(
            Volume: 1f,
            PitchCents: 0f,
            LowPassFilter: 0f,
            HighPassFilter: 0f,
            Priority: DefaultPriority,
            OutputBusId: 0);
    }
}
