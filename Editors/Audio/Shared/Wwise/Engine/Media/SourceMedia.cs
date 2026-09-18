namespace Editors.Audio.Shared.Wwise.Engine.Media
{
    // Decoded audio as the engine holds it: float samples at the rate and channel count the source
    // was authored in. Nothing here is expressed in the mix format — converting to it is the voice's
    // job, because rate conversion and pitch are the same operation and doing one at load would mean
    // the other could never exist.
    public sealed class SourceMedia
    {
        private readonly float[] _samples;

        public SourceMedia(
            string contentHash,
            float[] samples,
            int channelCount,
            int sampleRate,
            uint channelMask = 0,
            int? loopStartFrame = null,
            int? loopEndFrame = null)
            : this(
                contentHash,
                samples,
                channelCount,
                sampleRate,
                channelMask,
                loopStartFrame,
                loopEndFrame,
                shouldCopySamples: true)
        {
        }

        private SourceMedia(
            string contentHash,
            float[] samples,
            int channelCount,
            int sampleRate,
            uint channelMask,
            int? loopStartFrame,
            int? loopEndFrame,
            bool shouldCopySamples)
        {
            ArgumentNullException.ThrowIfNull(samples);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            if (samples.Length == 0)
                throw new ArgumentException("Source media must contain at least one sample frame.", nameof(samples));
            if (samples.Length % channelCount != 0)
                throw new ArgumentException("Source media must contain complete interleaved sample frames.", nameof(samples));
            if (samples.Any(sample => !float.IsFinite(sample)))
                throw new ArgumentException("Source media samples must be finite.", nameof(samples));

            var frameCount = samples.Length / channelCount;
            if (loopStartFrame.HasValue != loopEndFrame.HasValue)
                throw new ArgumentException("A source loop must provide both its start and end frame.");
            if (loopStartFrame.HasValue
                && (loopStartFrame.Value < 0
                    || loopEndFrame!.Value <= loopStartFrame.Value
                    || loopEndFrame.Value > frameCount))
                throw new ArgumentOutOfRangeException(nameof(loopEndFrame), "The source loop must be a non-empty range inside the media.");

            ContentHash = contentHash;
            _samples = shouldCopySamples ? samples.ToArray() : samples;
            ChannelCount = channelCount;
            SampleRate = sampleRate;
            ChannelMask = channelMask;
            LoopStartFrame = loopStartFrame;
            LoopEndFrame = loopEndFrame;
        }

        public string ContentHash { get; }

        // Interleaved, so a frame's channels sit side by side. Voices read this directly: there is
        // no byte buffer to reinterpret on the way in or out.
        public ReadOnlySpan<float> Samples => _samples;
        public int ChannelCount { get; }
        public int SampleRate { get; }
        public uint ChannelMask { get; }
        public int? LoopStartFrame { get; }
        public int? LoopEndFrame { get; }
        public bool HasLoopRegion => LoopStartFrame.HasValue;

        public int FrameCount => _samples.Length / ChannelCount;
        public TimeSpan Duration => TimeSpan.FromSeconds(FrameCount / (double)SampleRate);

        internal long Size => (long)_samples.Length * sizeof(float);
        internal long LastAccessSequence { get; set; }

        internal static SourceMedia FromOwnedSamples(
            string contentHash,
            float[] samples,
            int channelCount,
            int sampleRate,
            uint channelMask,
            int? loopStartFrame,
            int? loopEndFrame)
            => new(
                contentHash,
                samples,
                channelCount,
                sampleRate,
                channelMask,
                loopStartFrame,
                loopEndFrame,
                shouldCopySamples: false);
    }
}
