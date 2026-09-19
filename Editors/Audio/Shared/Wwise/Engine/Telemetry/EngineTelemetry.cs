namespace Editors.Audio.Shared.Wwise.Engine.Telemetry
{
    // What the engine did that nobody asked it to do, and how close to its limits it ran doing it.
    //
    // Everything here is counted on the render thread and read on the control thread, which is the
    // whole reason it is a counter block rather than a log call: formatting a message inside the
    // output callback is exactly the kind of work that makes a buffer late. The control side reads
    // these on a timer and says what changed.
    //
    // Counts are cumulative and never reset, so the control side reports the difference since it
    // last looked. Peaks are high-water marks for the same reason: a gauge sampled every 25 ms
    // would miss the moment worth knowing about.
    internal sealed class EngineTelemetry
    {
        internal readonly record struct BusPeak(uint BusId, float PeakLevel);
        private sealed record BusPeakState(uint[] BusIds, float[] PeakLevels);
        private long _unstartedVoiceCount;
        private long _stolenVoiceCount;
        private long _unscheduledEventCount;
        private long _silentCueCount;
        private long _blockOverrunCount;
        private long _unsupportedActionCount;
        private int _peakSoundingVoiceCount;
        private int _currentPhysicalVoiceCount;
        private int _currentVirtualVoiceCount;
        private int _peakPhysicalVoiceCount;
        private int _peakVirtualVoiceCount;
        private long _voiceThresholdTransitionCount;
        private long _unsupportedEffectCount;
        private long _unsupportedAuxiliarySendCount;
        private uint _highestPeakBusId;
        private volatile float _highestBusPeakLevel;
        private long _overLimitVirtualisedVoiceCount;
        private long _thresholdVirtualisedVoiceCount;
        private long _thresholdKilledVoiceCount;
        private BusPeakState _busPeakState = new([], []);
        private volatile float _masterPeakLevel;

        // Sounds that were asked for and never reached the mix, because there was no voice left
        // that the newcomer outranked, or that an equal-priority discard-oldest limit selected.
        public long UnstartedVoiceCount => Interlocked.Read(ref _unstartedVoiceCount);

        // Voices that gave up their place to something more important, and were ramped out rather
        // than cut for it.
        public long StolenVoiceCount => Interlocked.Read(ref _stolenVoiceCount);

        // Events the transport had no free slot to hold until their frame came round.
        public long UnscheduledEventCount => Interlocked.Read(ref _unscheduledEventCount);

        // Cues that fired onto media nobody warmed. A hard error rather than a shortfall: the audio
        // thread must never decode, so this is a gap in the warm logic.
        public long SilentCueCount => Interlocked.Read(ref _silentCueCount);

        // Output callbacks that took longer in wall-clock time than the audio they produced. The one
        // counter that catches the cause of a glitch rather than its symptom.
        public long BlockOverrunCount => Interlocked.Read(ref _blockOverrunCount);

        // Event actions this engine does not carry out. Counted rather than silently skipped,
        // because an unimplemented action looks exactly like a missing sound from the outside.
        public long UnsupportedActionCount => Interlocked.Read(ref _unsupportedActionCount);

        // The high-water mark rather than the live count: a gauge sampled every 25 ms would miss
        // the moment the pool actually filled, which is the moment worth knowing about.
        public int PeakSoundingVoiceCount => Volatile.Read(ref _peakSoundingVoiceCount);

        // Measured on the master bus before the limiter, so it says what the mix asked for rather
        // than what the limiter allowed out. Above 1.0 means the limiter earned its place.
        public float MasterPeakLevel => _masterPeakLevel;
        // A virtual voice keeps its scheduling and behaviour state without occupying a sounding
        // slot, so the two counts answer different questions: the physical one is how full the mix
        // is, the virtual one is how much the engine is still keeping track of behind it.
        public int CurrentPhysicalVoiceCount => Volatile.Read(ref _currentPhysicalVoiceCount);
        public int CurrentVirtualVoiceCount => Volatile.Read(ref _currentVirtualVoiceCount);
        public int PeakPhysicalVoiceCount => Volatile.Read(ref _peakPhysicalVoiceCount);
        public int PeakVirtualVoiceCount => Volatile.Read(ref _peakVirtualVoiceCount);

        // Crossings in both directions, so a voice flickering across the audibility threshold shows
        // up as a large count against a small peak rather than as nothing at all.
        public long VoiceThresholdTransitionCount => Interlocked.Read(ref _voiceThresholdTransitionCount);

        // Master-mixer effect slots and auxiliary sends the engine cannot reproduce. Counted at the
        // point the graph is built: the mix passes through dry, and says so, rather than presenting
        // itself as faithful.
        public long UnsupportedEffectCount => Interlocked.Read(ref _unsupportedEffectCount);
        public long UnsupportedAuxiliarySendCount => Interlocked.Read(ref _unsupportedAuxiliarySendCount);

        // The loudest any single bus has been, and which one it was. The master peak says the mix
        // is loud; this says where.
        public uint HighestPeakBusId => Volatile.Read(ref _highestPeakBusId);
        public float HighestBusPeakLevel => _highestBusPeakLevel;

        // Why a voice stopped sounding, kept apart because the three causes are fixed in three
        // different places: an ancestor's instance limit, its own level falling below the
        // audibility threshold, and the authored choice to be killed at that threshold rather than
        // virtualised.
        public long OverLimitVirtualisedVoiceCount => Interlocked.Read(ref _overLimitVirtualisedVoiceCount);
        public long ThresholdVirtualisedVoiceCount => Interlocked.Read(ref _thresholdVirtualisedVoiceCount);
        public long ThresholdKilledVoiceCount => Interlocked.Read(ref _thresholdKilledVoiceCount);

        // Per-bus high-water marks, in the graph's own order. Allocates, so it is a control-thread
        // read of render-thread state rather than anything the callback touches.
        public IReadOnlyList<BusPeak> BusPeaks
        {
            get
            {
                var state = Volatile.Read(ref _busPeakState);
                var peaks = new BusPeak[state.BusIds.Length];
                for (var index = 0; index < peaks.Length; index++)
                    peaks[index] = new BusPeak(state.BusIds[index], state.PeakLevels[index]);
                return peaks;
            }
        }

        public void CountUnstartedVoice() => Interlocked.Increment(ref _unstartedVoiceCount);

        public void CountUnstartedVoices(int count)
        {
            if (count > 0)
                Interlocked.Add(ref _unstartedVoiceCount, count);
        }

        public void CountStolenVoice() => Interlocked.Increment(ref _stolenVoiceCount);

        public void CountUnscheduledEvent() => Interlocked.Increment(ref _unscheduledEventCount);

        public void CountSilentCue() => Interlocked.Increment(ref _silentCueCount);

        public void CountBlockOverrun() => Interlocked.Increment(ref _blockOverrunCount);

        // Counted from the control thread, unlike the rest: warming is where an action is read.
        public void CountUnsupportedAction() => Interlocked.Increment(ref _unsupportedActionCount);

        public void CountUnsupportedEffects(int count)
        {
            if (count > 0)
                Interlocked.Add(ref _unsupportedEffectCount, count);
        }

        public void CountUnsupportedAuxiliarySends(int count)
        {
            if (count > 0)
                Interlocked.Add(ref _unsupportedAuxiliarySendCount, count);
        }

        public void CountVoiceThresholdTransition() => Interlocked.Increment(ref _voiceThresholdTransitionCount);
        public void CountOverLimitVirtualisedVoice() => Interlocked.Increment(ref _overLimitVirtualisedVoiceCount);
        public void CountThresholdVirtualisedVoice() => Interlocked.Increment(ref _thresholdVirtualisedVoiceCount);
        public void CountThresholdKilledVoice() => Interlocked.Increment(ref _thresholdKilledVoiceCount);

        public void ConfigureBusIds(IEnumerable<uint> busIds)
        {
            var ids = busIds.Distinct().OrderBy(id => id).ToArray();
            Volatile.Write(ref _busPeakState, new BusPeakState(ids, new float[ids.Length]));
        }

        // Written only from the render thread, which is why the peaks here can be a read and a
        // compare rather than a loop: nothing else is racing to raise them.
        public void RecordSoundingVoiceCount(int soundingVoiceCount)
        {
            if (soundingVoiceCount > _peakSoundingVoiceCount)
                Volatile.Write(ref _peakSoundingVoiceCount, soundingVoiceCount);
        }

        public void RecordVoiceCounts(int physicalVoiceCount, int virtualVoiceCount)
        {
            Volatile.Write(ref _currentPhysicalVoiceCount, physicalVoiceCount);
            Volatile.Write(ref _currentVirtualVoiceCount, virtualVoiceCount);
            if (physicalVoiceCount > _peakPhysicalVoiceCount)
                Volatile.Write(ref _peakPhysicalVoiceCount, physicalVoiceCount);
            if (virtualVoiceCount > _peakVirtualVoiceCount)
                Volatile.Write(ref _peakVirtualVoiceCount, virtualVoiceCount);
        }

        public void RecordBusPeak(uint busId, float peakLevel)
        {
            var state = Volatile.Read(ref _busPeakState);
            for (var index = 0; index < state.BusIds.Length; index++)
            {
                if (state.BusIds[index] == busId && peakLevel > state.PeakLevels[index])
                {
                    state.PeakLevels[index] = peakLevel;
                    break;
                }
            }
            if (peakLevel <= _highestBusPeakLevel)
                return;
            _highestBusPeakLevel = peakLevel;
            Volatile.Write(ref _highestPeakBusId, busId);
        }

        public void RecordMasterPeakLevel(float masterPeakLevel)
        {
            if (masterPeakLevel > _masterPeakLevel)
                _masterPeakLevel = masterPeakLevel;
        }
    }
}
