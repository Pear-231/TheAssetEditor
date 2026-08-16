using System.Runtime.InteropServices;

namespace Editors.Audio.Shared.Wwise.Engine.Mixing
{
    internal sealed class Voice
    {
        private long _voiceIdentifier;
        private long _playbackGeneration;
        private byte[] _pcmData;
        private long _currentPcmFrame;
        private long _initialPcmFrame;
        private long _firstPcmFrame;
        private long _timelineStartFrame;
        private long _firstAbsoluteOutputFrame;
        private bool _isLooping;
        private bool _isScheduledVoice;

        public bool IsActive => Volatile.Read(ref _pcmData) != null;

        public void Activate(
            long voiceIdentifier,
            long playbackGeneration,
            byte[] pcmData,
            long initialPcmFrame,
            bool isLooping,
            bool isScheduledVoice,
            long timelineStartFrame)
        {
            _voiceIdentifier = voiceIdentifier;
            _playbackGeneration = playbackGeneration;
            _currentPcmFrame = initialPcmFrame;
            _initialPcmFrame = initialPcmFrame;
            _firstPcmFrame = initialPcmFrame;
            _timelineStartFrame = timelineStartFrame;
            _firstAbsoluteOutputFrame = -1;
            _isLooping = isLooping;
            _isScheduledVoice = isScheduledVoice;
            Volatile.Write(ref _pcmData, pcmData);
        }

        public bool TryReadNextFrame(
            long absoluteOutputFrame,
            out float leftChannelSample,
            out float rightChannelSample,
            out VoiceHandle completedVoice)
        {
            leftChannelSample = 0;
            rightChannelSample = 0;
            completedVoice = default;
            var pcmData = Volatile.Read(ref _pcmData);
            if (pcmData == null)
                return false;

            var pcmSamples = MemoryMarshal.Cast<byte, float>(pcmData);
            var pcmFrameCount = pcmSamples.Length / PlaybackFormat.ChannelCount;
            if (_currentPcmFrame >= pcmFrameCount)
            {
                if (_isLooping && pcmFrameCount > 0)
                    _currentPcmFrame %= pcmFrameCount;
                else
                {
                    completedVoice = new VoiceHandle(_voiceIdentifier, _playbackGeneration);
                    Deactivate();
                    return false;
                }
            }

            var interleavedSampleIndex = (int)(_currentPcmFrame * PlaybackFormat.ChannelCount);
            if (_firstAbsoluteOutputFrame < 0)
            {
                _firstPcmFrame = _currentPcmFrame;
                Volatile.Write(ref _firstAbsoluteOutputFrame, absoluteOutputFrame);
            }
            leftChannelSample = pcmSamples[interleavedSampleIndex];
            rightChannelSample = pcmSamples[interleavedSampleIndex + 1];
            _currentPcmFrame++;
            return true;
        }

        public bool Matches(long voiceIdentifier, long playbackGeneration)
            => IsActive
                && _voiceIdentifier == voiceIdentifier
                && _playbackGeneration == playbackGeneration;

        public void Cancel(long voiceIdentifier, long playbackGeneration)
        {
            if (Matches(voiceIdentifier, playbackGeneration))
                Deactivate();
        }

        public void DeactivateIfScheduled()
        {
            if (_isScheduledVoice)
                Deactivate();
        }

        public void SeekImmediate(long playbackGeneration, long timelineFrame)
        {
            if (!IsActive || _isScheduledVoice || _playbackGeneration != playbackGeneration)
                return;

            _currentPcmFrame = Math.Max(
                0,
                _initialPcmFrame + timelineFrame - _timelineStartFrame);
            Volatile.Write(ref _firstAbsoluteOutputFrame, -1);
        }

        public void Deactivate()
        {
            Volatile.Write(ref _pcmData, null);
        }

        public bool TryGetPosition(
            VoiceHandle voiceHandle,
            long? absoluteOutputFrame,
            out long pcmFrame)
        {
            pcmFrame = 0;
            if (!IsActive
                || _voiceIdentifier != voiceHandle.VoiceIdentifier
                || _playbackGeneration != voiceHandle.PlaybackGeneration)
                return false;

            var pcmData = Volatile.Read(ref _pcmData);
            var firstAbsoluteOutputFrame = Interlocked.Read(ref _firstAbsoluteOutputFrame);
            if (!absoluteOutputFrame.HasValue || firstAbsoluteOutputFrame < 0 || pcmData == null)
            {
                pcmFrame = Interlocked.Read(ref _currentPcmFrame);
                return true;
            }

            var audibleFrameCount = Math.Max(0, absoluteOutputFrame.Value - firstAbsoluteOutputFrame);
            var pcmFrameCount = pcmData.Length / (PlaybackFormat.ChannelCount * sizeof(float));
            pcmFrame = _firstPcmFrame + audibleFrameCount;
            if (_isLooping && pcmFrameCount > 0)
                pcmFrame %= pcmFrameCount;
            else
                pcmFrame = Math.Min(pcmFrame, pcmFrameCount);
            return true;
        }
    }
}
