namespace Editors.Audio.Shared.Wwise.Engine.Mixing
{
    internal sealed class VoicePool
    {
        private readonly Voice[] _voices;

        public VoicePool(int maximumVoices)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumVoices);
            _voices = Enumerable.Range(0, maximumVoices).Select(_ => new Voice()).ToArray();
        }

        public bool TryActivate(
            long voiceIdentifier,
            long playbackGeneration,
            byte[] pcmData,
            long initialPcmFrame,
            bool isLooping,
            bool isScheduledVoice,
            long timelineStartFrame = 0)
        {
            foreach (var voice in _voices)
            {
                if (voice.IsActive)
                    continue;

                voice.Activate(
                    voiceIdentifier,
                    playbackGeneration,
                    pcmData,
                    initialPcmFrame,
                    isLooping,
                    isScheduledVoice,
                    timelineStartFrame);
                return true;
            }
            return false;
        }

        public int MixNextFrame(
            out float mixedLeftSample,
            out float mixedRightSample,
            long absoluteOutputFrame,
            System.Collections.Concurrent.ConcurrentQueue<VoiceCompletion> completedVoices)
        {
            mixedLeftSample = 0;
            mixedRightSample = 0;
            var activeVoiceCount = 0;

            foreach (var voice in _voices)
            {
                if (!voice.TryReadNextFrame(
                        absoluteOutputFrame,
                        out var voiceLeftSample,
                        out var voiceRightSample,
                        out var completedVoice))
                {
                    if (completedVoice != default)
                        completedVoices.Enqueue(new VoiceCompletion(completedVoice, absoluteOutputFrame));
                    continue;
                }
                mixedLeftSample += voiceLeftSample;
                mixedRightSample += voiceRightSample;
                activeVoiceCount++;
            }

            return activeVoiceCount;
        }

        public bool TryGetPosition(VoiceHandle voiceHandle, long? absoluteOutputFrame, out long pcmFrame)
        {
            foreach (var voice in _voices)
            {
                if (voice.TryGetPosition(voiceHandle, absoluteOutputFrame, out pcmFrame))
                    return true;
            }

            pcmFrame = 0;
            return false;
        }

        public bool HasActiveVoiceOtherThan(long voiceIdentifier, long playbackGeneration)
        {
            foreach (var voice in _voices)
            {
                if (voice.IsActive && !voice.Matches(voiceIdentifier, playbackGeneration))
                    return true;
            }
            return false;
        }

        public void CancelVoice(long voiceIdentifier, long playbackGeneration)
        {
            foreach (var voice in _voices)
            {
                voice.Cancel(voiceIdentifier, playbackGeneration);
            }
        }

        public void DeactivateScheduledVoices()
        {
            foreach (var voice in _voices)
            {
                voice.DeactivateIfScheduled();
            }
        }

        public void SeekImmediateVoices(long playbackGeneration, long timelineFrame)
        {
            foreach (var voice in _voices)
            {
                voice.SeekImmediate(playbackGeneration, timelineFrame);
            }
        }

        public void DeactivateAll()
        {
            foreach (var voice in _voices)
            {
                voice.Deactivate();
            }
        }
    }
}
