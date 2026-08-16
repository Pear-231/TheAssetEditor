using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Editors.Audio.Shared.Wwise.Engine.Output
{
    internal interface IAudioOutputDevice : IDisposable
    {
        bool EnsureCreated(ISampleProvider audioSampleSource);
        void EnsurePlaying();
        void FlushAndExecute(Action actionWhileOutputStopped);
        void ExecuteIfNotPlaying(Action actionWhileOutputNotPlaying);
        AudioDevicePosition? CaptureOutputPosition();
    }

    internal sealed class AudioOutputDevice : IAudioOutputDevice
    {
        private readonly object _outputPlayerLock = new();
        private readonly ILogger _logger = Logging.Create<AudioOutputDevice>();
        private IWavePlayer _outputPlayer;

        public bool EnsureCreated(ISampleProvider audioSampleSource)
        {
            if (_outputPlayer != null)
                return false;

            lock (_outputPlayerLock)
            {
                if (_outputPlayer != null)
                    return false;

                LogDefaultOutputMixFormat(audioSampleSource.WaveFormat);
                try { _outputPlayer = new WasapiOut(AudioClientShareMode.Shared, true, 50); }
                catch { _outputPlayer = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 3 }; }
                _outputPlayer.PlaybackStopped += OnPlaybackStopped;
                _outputPlayer.Init(new SampleToWaveProvider(audioSampleSource));
                return true;
            }
        }

        public void EnsurePlaying()
        {
            lock (_outputPlayerLock)
            {
                if (_outputPlayer?.PlaybackState == PlaybackState.Stopped)
                    _outputPlayer.Play();
            }
        }

        public void FlushAndExecute(Action actionWhileOutputStopped)
        {
            ArgumentNullException.ThrowIfNull(actionWhileOutputStopped);
            lock (_outputPlayerLock)
            {
                // Preserve the initialized player while flushing queued output. This
                // prevents a late/starved callback repeating its last audible buffer.
                try
                {
                    _outputPlayer?.Stop();
                }
                catch (Exception exception)
                {
                    _logger.Here().Error(exception, "Audio output failed while being flushed; the output device will be recreated on the next playback");
                    DisposeOutputPlayer();
                }
                actionWhileOutputStopped();
            }
        }

        public void ExecuteIfNotPlaying(Action actionWhileOutputNotPlaying)
        {
            ArgumentNullException.ThrowIfNull(actionWhileOutputNotPlaying);
            lock (_outputPlayerLock)
            {
                if (_outputPlayer?.PlaybackState == PlaybackState.Playing)
                    return;
                actionWhileOutputNotPlaying();
            }
        }

        public AudioDevicePosition? CaptureOutputPosition()
        {
            lock (_outputPlayerLock)
            {
                if (_outputPlayer is not IWavePosition outputWavePosition)
                    return null;
                try
                {
                    var averageBytesPerSecond = outputWavePosition.OutputWaveFormat.AverageBytesPerSecond;
                    if (averageBytesPerSecond > 0)
                        return new AudioDevicePosition(outputWavePosition.GetPosition(), averageBytesPerSecond);
                    else
                        return null;
                }
                catch
                {
                    return null;
                }
            }
        }

        private void LogDefaultOutputMixFormat(WaveFormat mixerFormat)
        {
            try
            {
                using var deviceEnumerator = new MMDeviceEnumerator();
                using var outputEndpoint = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                using var audioClient = outputEndpoint.AudioClient;
                var outputMixFormat = audioClient.MixFormat;
                _logger.Here().Information(
                    $"Audio output endpoint '{outputEndpoint.FriendlyName}' mixes at " +
                    $"{outputMixFormat.SampleRate} Hz, {outputMixFormat.Channels} channels, {outputMixFormat.BitsPerSample}-bit {outputMixFormat.Encoding}; " +
                    $"playback renders at {mixerFormat.SampleRate} Hz, {mixerFormat.Channels} channels, {mixerFormat.BitsPerSample}-bit {mixerFormat.Encoding}; " +
                    $"Windows sample rate conversion required {outputMixFormat.SampleRate != mixerFormat.SampleRate}");
            }
            catch (Exception exception)
            {
                _logger.Here().Warning(exception, "Could not query the default Windows output endpoint mix format");
            }
        }

        private void OnPlaybackStopped(object _, StoppedEventArgs eventArgs)
        {
            if (eventArgs.Exception != null)
                _logger.Here().Error(eventArgs.Exception, "Audio output device stopped unexpectedly");
        }

        private void DisposeOutputPlayer()
        {
            if (_outputPlayer == null)
                return;
            _outputPlayer.PlaybackStopped -= OnPlaybackStopped;
            _outputPlayer.Dispose();
            _outputPlayer = null;
        }

        public void Dispose()
        {
            lock (_outputPlayerLock)
            {
                _outputPlayer?.Stop();
                DisposeOutputPlayer();
            }
        }
    }
}
