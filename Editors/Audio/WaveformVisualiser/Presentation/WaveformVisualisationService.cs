using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using Editors.Audio.Shared.Wwise.Engine.Cache;
using NAudio.Wave;
using NAudio.WaveFormRenderer;
using Shared.GameFormats.Audio.Formats.Pcm;
using Color = System.Drawing.Color;
using DrawingImage = System.Drawing.Image;

namespace Editors.Audio.WaveformVisualiser.Presentation
{
    public sealed class WemWaveformSource(string cacheKey, Func<byte[]> loadBytes)
    {
        public string CacheKey { get; } = cacheKey;
        public Func<byte[]> LoadBytes { get; } = loadBytes;
    }

    public sealed record WaveformRenderResult(
        BitmapImage BaseImage,
        BitmapImage OverlayImage,
        TimeSpan TotalTime)
    {
        public int PixelWidth => BaseImage.PixelWidth;
    }

    public sealed class WaveformVisualisationService(SoundEngineCache soundEngineCache)
    {
        private readonly ConcurrentDictionary<string, WaveformRenderResult> _visualisationBySourceKey = new();
        private readonly ConcurrentDictionary<string, Task<WaveformRenderResult>> _renderInProgressByKey = new();
        private readonly ConcurrentDictionary<string, byte> _removedSourceKeys = new();

        public static int DefaultPixelsPerPeak { get; set; } = 2;
        public static int DefaultSpacerPixels { get; set; } = 1;

        public WaveformRenderResult Get(string sourceKey, int targetWidth)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
                return null;
            return _visualisationBySourceKey.TryGetValue(sourceKey, out var cached)
                && cached.PixelWidth == targetWidth
                    ? cached
                    : null;
        }

        public Task<WaveformRenderResult> GetOrRenderFileAsync(
            string filePath,
            int targetWidth,
            CancellationToken cancellationToken)
            => GetOrRenderAsync(
                filePath,
                targetWidth,
                () => soundEngineCache.GetFile(filePath),
                cancellationToken);

        public Task<WaveformRenderResult> GetOrRenderWemAsync(
            WemWaveformSource source,
            int targetWidth,
            CancellationToken cancellationToken)
            => GetOrRenderAsync(
                source.CacheKey,
                targetWidth,
                () => soundEngineCache.GetWem(source.LoadBytes()),
                cancellationToken);

        public Task<WaveformRenderResult> GetOrRenderAudioAsync(
            string sourceKey,
            SoundEngineCacheEntry audio,
            int targetWidth,
            CancellationToken cancellationToken)
            => GetOrRenderAsync(sourceKey, targetWidth, () => audio, cancellationToken);

        public Task PreloadFilesAsync(
            IEnumerable<string> filePaths,
            int targetWidth,
            CancellationToken cancellationToken)
            => PreloadAsync(
                (filePaths ?? [])
                    .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
                    .Distinct(StringComparer.OrdinalIgnoreCase),
                filePath => GetOrRenderFileAsync(filePath, targetWidth, cancellationToken),
                cancellationToken);

        public Task PreloadWemsAsync(
            IEnumerable<WemWaveformSource> sources,
            int targetWidth,
            CancellationToken cancellationToken)
            => PreloadAsync(
                (sources ?? [])
                    .Where(source => source != null && !string.IsNullOrWhiteSpace(source.CacheKey))
                    .DistinctBy(source => source.CacheKey, StringComparer.OrdinalIgnoreCase),
                source => GetOrRenderWemAsync(source, targetWidth, cancellationToken),
                cancellationToken);

        public void Remove(string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
                return;
            _removedSourceKeys[sourceKey] = 0;
            _visualisationBySourceKey.TryRemove(sourceKey, out _);
        }

        private async Task<WaveformRenderResult> GetOrRenderAsync(
            string sourceKey,
            int targetWidth,
            Func<SoundEngineCacheEntry> loadAudio,
            CancellationToken cancellationToken)
        {
            var cached = Get(sourceKey, targetWidth);
            if (cached != null)
                return cached;

            _removedSourceKeys.TryRemove(sourceKey, out _);
            var renderKey = $"{sourceKey}|{targetWidth}";
            var renderTask = _renderInProgressByKey.GetOrAdd(
                renderKey,
                _ => Task.Run(() => Render(loadAudio().Audio, targetWidth)));

            try
            {
                var result = await renderTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (!_removedSourceKeys.ContainsKey(sourceKey))
                    _visualisationBySourceKey[sourceKey] = result;
                return result;
            }
            finally
            {
                if (renderTask.IsCompleted)
                    _renderInProgressByKey.TryRemove(
                        new KeyValuePair<string, Task<WaveformRenderResult>>(renderKey, renderTask));
            }
        }

        private static async Task PreloadAsync<TSource>(
            IEnumerable<TSource> sources,
            Func<TSource, Task<WaveformRenderResult>> render,
            CancellationToken cancellationToken)
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                CancellationToken = cancellationToken
            };

            try
            {
                await Parallel.ForEachAsync(sources, options, async (source, _) =>
                {
                    await render(source).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static WaveformRenderResult Render(PcmAudio audio, int targetWidth)
        {
            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat((int)audio.SampleRate, audio.Channels);
            var baseWaveformBitmap = RenderWaveformImage(audio.Data, waveFormat, CreateBaseWaveformSettings(targetWidth));
            var overlayWaveformBitmap = RenderWaveformImage(audio.Data, waveFormat, CreateOverlayWaveformSettings(targetWidth));
            return new WaveformRenderResult(
                baseWaveformBitmap,
                overlayWaveformBitmap,
                TimeSpan.FromSeconds(audio.SampleCount / (double)audio.SampleRate));
        }

        private static BitmapImage RenderWaveformImage(byte[] pcmData, WaveFormat waveFormat, WaveFormRendererSettings settings)
        {
            using var pcmStream = new MemoryStream(pcmData, writable: false);
            using var waveStream = new RawSourceWaveStream(pcmStream, waveFormat);
            using var alignedWaveStream = new BlockAlignReductionStream(waveStream);
            using var drawingImage = new WaveFormRenderer().Render(alignedWaveStream, settings);
            return ToBitmapImage(drawingImage);
        }

        private static SoundCloudBlockWaveFormSettings CreateBaseWaveformSettings(int width)
            => new(
                Color.FromArgb(196, 230, 230, 230), // top peak
                Color.FromArgb(64, 220, 220, 220), // top spacer
                Color.FromArgb(196, 210, 210, 210), // bottom peak
                Color.FromArgb(64, 190, 190, 190)) // bottom spacer
            {
                Width = width,
                PixelsPerPeak = DefaultPixelsPerPeak,
                SpacerPixels = DefaultSpacerPixels,
                TopSpacerGradientStartColor = Color.FromArgb(64, 220, 220, 220),
                BackgroundColor = Color.Transparent
            };

        private static SoundCloudBlockWaveFormSettings CreateOverlayWaveformSettings(int width)
            => new(
                Color.FromArgb(255, 255, 68, 0), // top peak
                Color.FromArgb(64, 255, 68, 0), // top spacer
                Color.FromArgb(255, 255, 191, 153), // bottom peak
                Color.FromArgb(128, 255, 191, 153)) // bottom spacer
            {
                Width = width,
                PixelsPerPeak = DefaultPixelsPerPeak,
                SpacerPixels = DefaultSpacerPixels,
                TopSpacerGradientStartColor = Color.FromArgb(64, 255, 68, 0),
                BackgroundColor = Color.Transparent
            };

        private static BitmapImage ToBitmapImage(DrawingImage drawingImage)
        {
            using var memoryStream = new MemoryStream();
            try
            {
                drawingImage.Save(memoryStream, ImageFormat.Png);
            }
            catch (ArgumentNullException)
            {
                // Sometimes the encoder isn't initialised at the start so we delay then retry.
                Thread.Sleep(50);
                drawingImage.Save(memoryStream, ImageFormat.Png);
            }

            memoryStream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = memoryStream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
