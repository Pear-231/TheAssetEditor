namespace Editors.Audio.Shared.Wwise.Engine
{
    internal enum PostOutcome
    {
        None,
        Played,
        Refused,
        Silent
    }

    // Allocated on the control thread with the command and completed in place by the render thread.
    // The completion poll reads it directly, so publishing a terminal post never grows a collection
    // or allocates a ConcurrentQueue segment inside the output callback.
    internal sealed class PostCompletionState(PlayingId playingId)
    {
        private long _absoluteOutputFrame = -1;
        private int _completionClaimed;
        private int _outcome;

        public PlayingId PlayingId { get; } = playingId;

        public bool IsCompleted => Volatile.Read(ref _outcome) != (int)PostOutcome.None;

        public void Complete(PostOutcome outcome, long absoluteOutputFrame)
        {
            if (outcome == PostOutcome.None)
                throw new ArgumentOutOfRangeException(nameof(outcome));

            if (Interlocked.CompareExchange(ref _completionClaimed, 1, 0) != 0)
                return;

            Interlocked.Exchange(ref _absoluteOutputFrame, absoluteOutputFrame);
            Volatile.Write(ref _outcome, (int)outcome);
        }

        public bool TryGetCompletion(out PostOutcome outcome, out long absoluteOutputFrame)
        {
            outcome = (PostOutcome)Volatile.Read(ref _outcome);
            absoluteOutputFrame = Interlocked.Read(ref _absoluteOutputFrame);
            return outcome != PostOutcome.None;
        }
    }
}
