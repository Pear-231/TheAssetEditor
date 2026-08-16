using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Events.AudioFilesExplorer;
using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Cache;
using Editors.Audio.Shared.Wwise.Engine.Mixing;
using Editors.Audio.WaveformVisualiser.Events;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Ui.Common;

namespace Editors.Audio.WaveformVisualiser.Presentation
{
    public partial class WaveformVisualiserViewModel : ObservableObject, IDisposable
    {
        private readonly IEventHub _eventHub;
        private readonly LocalizationManager _localisationManager;
        private readonly ISoundEngine _soundEngine;
        private readonly SoundEngineCache _soundEngineCache;
        private readonly WaveformVisualisationService _waveformVisualisationService;

        private static readonly TimeSpan s_waveformResizeDebounceDelay = TimeSpan.FromMilliseconds(200);

        private readonly List<string> _currentPlaylistFilePaths = [];

        private bool _isWaveformPlayheadRenderingEnabled;
        private CancellationTokenSource _waveformRenderCancellationTokenSource;
        private CancellationTokenSource _waveformResizeDebounceCancellationTokenSource;

        private DateTime _lastPlaybackTimerTextUpdateUtc = DateTime.MinValue;

        private string _currentFilePathKey;
        private SoundEngineCacheEntry _currentAudio;
        private int _currentPlaylistIndex = -1;
        private VoiceHandle _playingVoice;


        [ObservableProperty] private string _waveformVisualiserLabel;
        [ObservableProperty] private int _waveformPixelWidth;
        [ObservableProperty] private int _waveformPixelHeight;
        [ObservableProperty] private ImageSource _audioWaveformBaseImageSource;
        [ObservableProperty] private ImageSource _audioWaveformOverlayImageSource;
        [ObservableProperty] private Rect _audioWaveformOverlayClip;
        [ObservableProperty] private double _hostWidth;
        [ObservableProperty] private TimeSpan _currentPlaybackTime = TimeSpan.Zero;
        [ObservableProperty] private TimeSpan _totalPlaybackTime = TimeSpan.Zero;
        [ObservableProperty] private bool _isPlaying;
        [ObservableProperty] private string _playPauseButtonText;

        public WaveformVisualiserViewModel(
            IEventHub eventHub,
            LocalizationManager localizationManager,
            ISoundEngine soundEngine,
            SoundEngineCache soundEngineCache,
            WaveformVisualisationService waveformVisualisationService)
        {
            _eventHub = eventHub;
            _localisationManager = localizationManager;
            _soundEngine = soundEngine;
            _soundEngineCache = soundEngineCache;
            _waveformVisualisationService = waveformVisualisationService;

            _eventHub.Register<AudioFilesExplorerNodeSelectedEvent>(this, AudioFilesExplorerNodeSelected);
            _eventHub.Register<AudioFilesChangedEvent>(this, OnAudioFilesChanged);
            _eventHub.Register<PlayAudioRequestedEvent>(this, OnPlayAudioRequested);
            _eventHub.Register<CacheWaveformRequestedEvent>(this, OnCacheWaveformRequested);
            _eventHub.Register<DecacheWaveformRequestedEvent>(this, OnDecacheWaveformRequested);

            _soundEngine.VoiceCompleted += OnVoiceCompleted;

            AudioWaveformOverlayClip = new Rect(0, 0, 0, 0);

            UpdateWaveformVisualiserLabel();
            UpdatePlayPauseButtonText();
        }

        partial void OnIsPlayingChanged(bool value) => UpdatePlayPauseButtonText();

        public void AudioFilesExplorerNodeSelected(AudioFilesExplorerNodeSelectedEvent e) => SetSelectedPlaylist(e.WavFilePaths);

        public void OnAudioFilesChanged(AudioFilesChangedEvent e)
        {
            var wavFilePaths = e.AudioFiles
                .Select(audioFile => audioFile.WavPackFilePath)
                .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
                .ToList();
            SetSelectedPlaylist(wavFilePaths);
        }

        public void OnPlayAudioRequested(PlayAudioRequestedEvent e)
        {
            SetSelectedPlaylist(e.WavFilePaths);
            _ = Play();
        }

        public void OnCacheWaveformRequested(CacheWaveformRequestedEvent e)
        {
            LoadWaveformImagesIntoCacheForCurrentWidth(e.FilePaths);
        }

        public void OnDecacheWaveformRequested(DecacheWaveformRequestedEvent e)
        {
            var filePathsInUse = new HashSet<string>(_currentPlaylistFilePaths, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(_currentFilePathKey))
                filePathsInUse.Add(_currentFilePathKey);

            foreach (var filePath in e.FilePaths)
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    continue;

                if (filePathsInUse.Contains(filePath))
                    continue;

                _waveformVisualisationService.Remove(filePath);
            }
        }

        // Stops what this visualiser is playing and rewinds the playhead, leaving the
        // displayed waveform alone. Anything else the engine is playing keeps running.
        public void StopPlayback()
        {
            StopWaveformPlayheadRendering();
            StopOwnedPlayback();
            CurrentPlaybackTime = TimeSpan.Zero;
            IsPlaying = false;
            ResetWaveformPlayheadAndProgress();
        }

        public async Task LoadFromWemSourceAsync(WemWaveformSource source, string labelKey)
        {
            StopPlayback();
            _currentAudio = null;

            _currentFilePathKey = string.Empty;
            _currentPlaylistFilePaths.Clear();
            _currentPlaylistIndex = -1;
            WaveformVisualiserLabel = labelKey;
            TotalPlaybackTime = TimeSpan.Zero;

            var cancellationToken = BeginWaveformRenderOperation();

            try
            {
                var wemBytes = await Task.Run(source.LoadBytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (wemBytes == null || wemBytes.Length == 0)
                    return;

                _currentAudio = await Task.Run(() => _soundEngineCache.GetWem(wemBytes), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var targetWidth = GetTargetWidth();
                var result = await _waveformVisualisationService
                    .GetOrRenderAudioAsync(source.CacheKey, _currentAudio, targetWidth, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                TotalPlaybackTime = result.TotalTime;
                ApplyWaveformBitmaps(result.BaseImage, result.OverlayImage);
            }
            catch (OperationCanceledException) { }
        }

        public void PreloadWemWaveforms(IEnumerable<WemWaveformSource> sources)
        {
            var targetWidth = GetTargetWidth();
            _ = _waveformVisualisationService.PreloadWemsAsync(
                sources,
                targetWidth,
                CancellationToken.None);
        }

        public void SetSelectedPlaylist(List<string> filePaths)
        {
            StopPlayback();
            _currentAudio = null;

            _currentPlaylistFilePaths.Clear();
            if (filePaths != null)
            {
                var validDistinctPaths = filePaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var path in validDistinctPaths)
                    _currentPlaylistFilePaths.Add(path);
            }

            if (_currentPlaylistFilePaths.Count > 0)
                _currentPlaylistIndex = 0;
            else
                _currentPlaylistIndex = -1;

            LoadWaveformImagesIntoCacheForCurrentWidth(_currentPlaylistFilePaths);

            if (_currentPlaylistIndex >= 0)
            {
                _currentFilePathKey = _currentPlaylistFilePaths[_currentPlaylistIndex];
                UpdateWaveformVisualiserLabel();
                TotalPlaybackTime = TimeSpan.Zero;
                ResetWaveformPlayheadAndProgress();
                _ = RenderWaveformPreviewAsync();
            }
            else
            {
                _currentFilePathKey = string.Empty;
                UpdateWaveformVisualiserLabel();
                TotalPlaybackTime = TimeSpan.Zero;
                ResetWaveformPlayheadAndProgress();
            }
        }

        partial void OnHostWidthChanged(double value)
        {
            var previousCancellationToken = Interlocked.Exchange(ref _waveformResizeDebounceCancellationTokenSource, new CancellationTokenSource());
            if (previousCancellationToken != null)
            {
                previousCancellationToken.Cancel();
                previousCancellationToken.Dispose();
            }

            var cancellationToken = _waveformResizeDebounceCancellationTokenSource.Token;

            _ = RebuildWaveformsAfterResizeAsync(cancellationToken);
        }

        private async Task RebuildWaveformsAfterResizeAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(s_waveformResizeDebounceDelay, cancellationToken);
                RebuildCacheForCurrentWidthExcludingCurrent();

                if (!string.IsNullOrWhiteSpace(_currentFilePathKey))
                    await RenderWaveformPreviewAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void RebuildCacheForCurrentWidthExcludingCurrent()
        {
            var targetWidth = GetTargetWidth();

            var filePathsNeedingRebuild = _currentPlaylistFilePaths
                .Where(filePath => _waveformVisualisationService.Get(filePath, targetWidth) == null)
                .Where(filePath => !string.Equals(filePath, _currentFilePathKey, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (filePathsNeedingRebuild.Length == 0)
                return;

            LoadWaveformImagesIntoCacheForCurrentWidth(filePathsNeedingRebuild);
        }

        [RelayCommand]
        private void PlayPause()
        {
            if (OwnsCurrentPlayback && _soundEngine.PlaybackState == SoundPlaybackState.Playing)
            {
                IsPlaying = false;
                Pause();
            }
            else
            {
                _ = Play();
            }
        }

        [RelayCommand]
        private async Task Play()
        {
            if (_currentAudio == null && string.IsNullOrWhiteSpace(_currentFilePathKey))
                return;

            if (OwnsCurrentPlayback && _soundEngine.PlaybackState == SoundPlaybackState.Playing)
                return;

            if (!OwnsCurrentPlayback || _soundEngine.PlaybackState == SoundPlaybackState.Stopped)
            {
                var requestedFilePath = _currentFilePathKey;
                var audio = _currentAudio;
                if (audio == null)
                {
                    var loadedAudio = await Task.Run(() => _soundEngineCache.GetFile(requestedFilePath));
                    if (!string.Equals(requestedFilePath, _currentFilePathKey, StringComparison.OrdinalIgnoreCase))
                        return;

                    _currentAudio ??= loadedAudio;
                    audio = _currentAudio;
                }

                if (!ReferenceEquals(audio, _currentAudio))
                    return;

                CurrentPlaybackTime = TimeSpan.Zero;
                ResetWaveformPlayheadAndProgress();
                // Cancel only this visualiser's own voice: anything else the engine is
                // playing, such as an animation timeline, must keep running.
                CancelPlayingVoice();
                _playingVoice = _soundEngine.Play(audio);
            }
            else if (_soundEngine.PlaybackState == SoundPlaybackState.Paused)
                _soundEngine.Resume();

            if (_soundEngine.PlaybackState == SoundPlaybackState.Playing)
                StartWaveformPlayheadRendering();
            else
                StopWaveformPlayheadRendering();

            IsPlaying = _soundEngine.PlaybackState == SoundPlaybackState.Playing;
        }

        [RelayCommand]
        private void Pause()
        {
            if (!OwnsCurrentPlayback || _soundEngine.PlaybackState != SoundPlaybackState.Playing)
                return;

            IsPlaying = false;
            _soundEngine.Pause();
            StopWaveformPlayheadRendering();
        }

        private async Task RenderWaveformPreviewAsync()
        {
            var cancellationToken = BeginWaveformRenderOperation();
            var filePathKey = _currentFilePathKey;
            if (string.IsNullOrWhiteSpace(filePathKey))
                return;

            try
            {
                var result = await _waveformVisualisationService.GetOrRenderFileAsync(
                    filePathKey,
                    GetTargetWidth(),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ApplyWaveformBitmaps(result.BaseImage, result.OverlayImage);
                TotalPlaybackTime = result.TotalTime;
            }
            catch (OperationCanceledException) { }
        }


        private void ApplyWaveformBitmaps(BitmapImage baseImage, BitmapImage overlayImage)
        {
            void Apply()
            {
                AudioWaveformBaseImageSource = baseImage;
                AudioWaveformOverlayImageSource = overlayImage;

                WaveformPixelWidth = baseImage.PixelWidth;
                WaveformPixelHeight = baseImage.PixelHeight;

                AudioWaveformOverlayClip = new Rect(0, 0, 0, WaveformPixelHeight);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(Apply);
            else
                Apply();
        }

        private int GetTargetWidth()
        {
            var hostWidth = HostWidth;
            if (hostWidth > 0)
                return (int)Math.Max(300, hostWidth);
            return 800;
        }

        private void StartWaveformPlayheadRendering()
        {
            if (_isWaveformPlayheadRenderingEnabled)
                return;

            CompositionTarget.Rendering += OnCompositionTargetRenderingForWaveformPlayhead;
            _isWaveformPlayheadRenderingEnabled = true;
        }

        private void StopWaveformPlayheadRendering()
        {
            if (!_isWaveformPlayheadRenderingEnabled)
                return;

            CompositionTarget.Rendering -= OnCompositionTargetRenderingForWaveformPlayhead;
            _isWaveformPlayheadRenderingEnabled = false;
        }

        private void OnCompositionTargetRenderingForWaveformPlayhead(object sender, EventArgs e)
        {
            if (!_isWaveformPlayheadRenderingEnabled ||
                !OwnsCurrentPlayback ||
                _soundEngine.PlaybackState != SoundPlaybackState.Playing ||
                WaveformPixelWidth <= 0)
                return;

            var totalTime = TotalPlaybackTime;
            if (totalTime <= TimeSpan.Zero)
                return;

            var playbackPosition = _soundEngine.GetPosition(_playingVoice);
            if (!playbackPosition.HasValue)
                return;
            var displayedPosition = TimeSpan.FromSeconds(Math.Clamp(
                playbackPosition.Value.TotalSeconds,
                0,
                totalTime.TotalSeconds));
            var ratio = displayedPosition.TotalSeconds / totalTime.TotalSeconds;
            var playedWidthPx = ratio * WaveformPixelWidth;

            AudioWaveformOverlayClip = new Rect(0, 0, playedWidthPx, WaveformPixelHeight);

            var timeNow = DateTime.UtcNow;
            if ((timeNow - _lastPlaybackTimerTextUpdateUtc).TotalMilliseconds >= 50)
            {
                _lastPlaybackTimerTextUpdateUtc = timeNow;
                CurrentPlaybackTime = displayedPosition;
            }
        }

        public void SetSelectedFilePath(string filePath)
        {
            StopPlayback();
            _currentAudio = null;

            _currentFilePathKey = filePath;
            UpdateWaveformVisualiserLabel();
            TotalPlaybackTime = TimeSpan.Zero;

            _ = RenderWaveformPreviewAsync();
        }

        private void OnVoiceCompleted(VoiceHandle voiceHandle)
        {
            if (_playingVoice == default || _playingVoice != voiceHandle)
                return;

            void CompleteVoice()
            {
                if (_playingVoice != voiceHandle)
                    return;
                _playingVoice = default;
                ResetWaveformPlayheadAndProgress();
                StopWaveformPlayheadRendering();
                CurrentPlaybackTime = TimeSpan.Zero;
                IsPlaying = false;

                if (_currentPlaylistFilePaths.Count == 0)
                    return;

                var nextIndex = _currentPlaylistIndex + 1;
                if (nextIndex >= 0 && nextIndex < _currentPlaylistFilePaths.Count)
                {
                    _currentPlaylistIndex = nextIndex;
                    var nextPath = _currentPlaylistFilePaths[_currentPlaylistIndex];

                    SetSelectedFilePath(nextPath);
                    _ = Play();
                }
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(CompleteVoice);
            else
                CompleteVoice();
        }

        private bool OwnsCurrentPlayback
            => _playingVoice != default
                && _playingVoice.PlaybackGeneration == _soundEngine.PlaybackGeneration;

        private void StopOwnedPlayback()
        {
            if (!OwnsCurrentPlayback)
                return;
            CancelPlayingVoice();
        }

        // Voice identifiers are only meaningful within the generation that produced them, so
        // a voice from a generation something else has replaced is already gone.
        private void CancelPlayingVoice()
        {
            if (_playingVoice != default && OwnsCurrentPlayback)
                _soundEngine.Cancel(_playingVoice);
            _playingVoice = default;
        }

        private void ResetWaveformPlayheadAndProgress() => AudioWaveformOverlayClip = new Rect(0, 0, 0, WaveformPixelHeight);

        private void LoadWaveformImagesIntoCacheForCurrentWidth(IEnumerable<string> filePaths)
        {
            var targetWidth = GetTargetWidth();

            _ = _waveformVisualisationService.PreloadFilesAsync(filePaths, targetWidth, CancellationToken.None);
        }

        private void UpdateWaveformVisualiserLabel()
        {
            if (string.IsNullOrWhiteSpace(_currentFilePathKey))
                WaveformVisualiserLabel = "Sound Engine";
            else
            {
                var fileName = Path.GetFileName(_currentFilePathKey);
                WaveformVisualiserLabel = $"Sound Engine – {WpfHelpers.DuplicateUnderscores(fileName)}";
            }
        }

        private void UpdatePlayPauseButtonText()
        {
            PlayPauseButtonText = IsPlaying ? _localisationManager.Get("WaveformVisualiser.Pause") : _localisationManager.Get("WaveformVisualiser.Play");
        }

        public void Dispose()
        {
            StopWaveformPlayheadRendering();
            _soundEngine.VoiceCompleted -= OnVoiceCompleted;
            StopOwnedPlayback();
            _currentAudio = null;
            _eventHub.UnRegister(this);

            if (_waveformRenderCancellationTokenSource != null)
            {
                _waveformRenderCancellationTokenSource.Cancel();
                _waveformRenderCancellationTokenSource.Dispose();
            }

            if (_waveformResizeDebounceCancellationTokenSource != null)
            {
                _waveformResizeDebounceCancellationTokenSource.Cancel();
                _waveformResizeDebounceCancellationTokenSource.Dispose();
            }
        }

        public void SetSelectedHostWidth(double width) => HostWidth = width;

        private CancellationToken BeginWaveformRenderOperation()
        {
            var currentCancellation = new CancellationTokenSource();
            var previousCancellation = Interlocked.Exchange(
                ref _waveformRenderCancellationTokenSource,
                currentCancellation);
            if (previousCancellation != null)
            {
                previousCancellation.Cancel();
                previousCancellation.Dispose();
            }
            return currentCancellation.Token;
        }
    }
}
