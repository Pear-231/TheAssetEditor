namespace Editors.Audio.Shared.Wwise.Engine.Timing
{
    internal sealed class OutputTimelineLedger
    {
        private const int LedgerCapacity = 65_536;
        private const int LedgerIndexMask = LedgerCapacity - 1;

        private readonly long[] _publishedOutputFrameMarkers = new long[LedgerCapacity];
        private readonly long[] _playbackGenerations = new long[LedgerCapacity];
        private readonly long[] _timelineFrames = new long[LedgerCapacity];

        public void PublishFrameMapping(
            long absoluteOutputFrame,
            long playbackGeneration,
            long timelineFrame)
        {
            var ledgerIndex = (int)(absoluteOutputFrame & LedgerIndexMask);
            _playbackGenerations[ledgerIndex] = playbackGeneration;
            _timelineFrames[ledgerIndex] = timelineFrame;
            Volatile.Write(ref _publishedOutputFrameMarkers[ledgerIndex], absoluteOutputFrame + 1);
        }

        public bool TryGetTimelineFrameForOutputFrame(
            long absoluteOutputFrame,
            long playbackGeneration,
            out long mappedTimelineFrame)
        {
            mappedTimelineFrame = 0;
            if (absoluteOutputFrame < 0)
                return false;

            var ledgerIndex = (int)(absoluteOutputFrame & LedgerIndexMask);
            var expectedOutputFrameMarker = absoluteOutputFrame + 1;
            if (Volatile.Read(ref _publishedOutputFrameMarkers[ledgerIndex]) != expectedOutputFrameMarker)
                return false;

            var storedPlaybackGeneration = _playbackGenerations[ledgerIndex];
            var storedTimelineFrame = _timelineFrames[ledgerIndex];
            if (Volatile.Read(ref _publishedOutputFrameMarkers[ledgerIndex]) != expectedOutputFrameMarker
                || storedPlaybackGeneration != playbackGeneration)
                return false;

            mappedTimelineFrame = storedTimelineFrame;
            return true;
        }
    }
}
