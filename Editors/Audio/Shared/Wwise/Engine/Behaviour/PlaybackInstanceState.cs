namespace Editors.Audio.Shared.Wwise.Engine.Behaviour
{
    // How far a container has got and what it has lately played, held per node and per game object.
    //
    // It cannot live on the HIRC objects: those are shared reference data that the editor writes
    // back to packs, and two game objects playing the same container have to keep separate cursors.
    //
    // Created on the control thread at warm time and then advanced only on the audio thread, at cue
    // time. Everything here is allocation free once constructed, because that is where it runs.
    internal sealed class PlaybackInstanceState
    {
        private readonly uint[] _recentlyPlayedNodeIds;
        private int _recentlyPlayedCount;
        private int _recentlyPlayedCursor;
        private int _sequenceIndex;
        private uint _randomState;
        private readonly bool[] _shuffleEntriesPlayed;
        private int _shufflePlayedCount;

        // Seeded from the node rather than from the clock, so a session repeats itself when it is
        // replayed. Variation within a pass is what the game hears; unpredictability between runs
        // would only make a report of what was heard impossible to follow up.
        public PlaybackInstanceState(ContainerInstanceKey key, int avoidRepeatCount, int childCount, bool isShuffle)
        {
            _recentlyPlayedNodeIds = new uint[Math.Max(0, avoidRepeatCount)];
            _shuffleEntriesPlayed = isShuffle ? new bool[Math.Max(0, childCount)] : [];
            _randomState = Seed(key);
        }

        // Where a sequence container has got to, and where a random one would carry on from if the
        // pick it wanted could not sound.
        public int SequenceIndex
        {
            get => _sequenceIndex;
            set => _sequenceIndex = value;
        }

        public bool WasRecentlyPlayed(uint nodeId)
        {
            for (var index = 0; index < _recentlyPlayedCount; index++)
            {
                if (_recentlyPlayedNodeIds[index] == nodeId)
                    return true;
            }
            return false;
        }

        public void RecordPlayed(uint nodeId)
        {
            if (_recentlyPlayedNodeIds.Length == 0)
                return;

            _recentlyPlayedNodeIds[_recentlyPlayedCursor] = nodeId;
            _recentlyPlayedCursor = (_recentlyPlayedCursor + 1) % _recentlyPlayedNodeIds.Length;
            if (_recentlyPlayedCount < _recentlyPlayedNodeIds.Length)
                _recentlyPlayedCount++;
        }


        public bool IsShuffleEntryAvailable(int childOrdinal)
            => _shuffleEntriesPlayed.Length == 0 || !_shuffleEntriesPlayed[childOrdinal];

        public void RecordShuffleEntry(int childOrdinal)
        {
            if (_shuffleEntriesPlayed.Length == 0 || _shuffleEntriesPlayed[childOrdinal])
                return;

            _shuffleEntriesPlayed[childOrdinal] = true;
            _shufflePlayedCount++;
        }

        // The history is deliberately retained when the bag empties. In shuffle mode Wwise applies
        // avoid-repeat at the reset boundary, which is the only point where an exhausted bag can
        // otherwise immediately repeat its final entry.
        public void ResetShuffleBag()
        {
            if (_shufflePlayedCount < _shuffleEntriesPlayed.Length)
                return;

            Array.Clear(_shuffleEntriesPlayed);
            _shufflePlayedCount = 0;
        }

        // Xorshift rather than Random, because this is called on the audio thread and Random.Shared
        // is neither allocation free in every path nor reproducible.
        public int NextRandom(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 1)
                return 0;

            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (int)(_randomState % (uint)exclusiveUpperBound);
        }


        private static uint Seed(ContainerInstanceKey key)
        {
            var hash = 2_166_136_261u;
            foreach (var character in key.BankPath ?? string.Empty)
            {
                hash ^= char.ToUpperInvariant(character);
                hash *= 16_777_619u;
            }

            hash ^= key.NodeId;
            hash *= 16_777_619u;
            return hash == 0 ? 0x9E3779B9u : hash;
        }
    }
}
