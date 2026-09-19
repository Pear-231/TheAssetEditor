namespace Editors.Audio.Shared.Wwise.Engine.Behaviour
{
    // Switch and state changes an event's actions performed, on their way from the cue that fired
    // them to the control thread that can act on them.
    //
    // A SetSwitch or SetState action fires inside the render callback, and what it changes — the
    // registry, and every plan warmed against the old value — is control-thread data that the audio
    // thread must not touch. So the audio thread hands the change over and the control thread makes
    // it, on the poll it already runs. The cost is that the change lands within one poll interval of
    // the cue rather than exactly on it; warming has already resolved the branches the event itself
    // reaches, so what waits is only what a *later* post would select.
    //
    // A fixed ring, written by the render thread and read by the control thread, so publishing a
    // transition allocates nothing and cannot block.
    internal sealed class PendingTransitionQueue
    {
        private readonly ResolvedTransition[] _transitions;
        private long _publishedCount;
        private long _takenCount;

        public PendingTransitionQueue(int capacity = 64)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            _transitions = new ResolvedTransition[capacity];
        }

        // Returns whether there was room. A full queue drops the change rather than growing inside
        // the render callback, and dropping it is visible: the group stays where it was.
        public bool TryPublish(in ResolvedTransition transition)
        {
            var publishedCount = Interlocked.Read(ref _publishedCount);
            if (publishedCount - Interlocked.Read(ref _takenCount) >= _transitions.Length)
                return false;

            _transitions[(int)(publishedCount % _transitions.Length)] = transition;
            Interlocked.Increment(ref _publishedCount);
            return true;
        }

        public bool TryTake(out ResolvedTransition transition)
        {
            var takenCount = Interlocked.Read(ref _takenCount);
            if (takenCount >= Interlocked.Read(ref _publishedCount))
            {
                transition = default;
                return false;
            }

            transition = _transitions[(int)(takenCount % _transitions.Length)];
            Interlocked.Increment(ref _takenCount);
            return true;
        }
    }
}
