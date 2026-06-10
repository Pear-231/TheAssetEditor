using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.CompressedMap;

namespace Editors.BmdEditor.Services
{
    internal sealed class BmdTerrainHeightMap
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public float[,] Heights { get; init; } = new float[0, 0];
        public float WorldSize { get; init; } = 2048f;
        public string SourcePath { get; init; } = string.Empty;
    }

    internal static class BmdTerrainLoader
    {
        private const float DefaultWorldSize = 2048f;
        private const float DefaultMaxHeight = 64f;

        public static BmdTerrainHeightMap? TryLoad(
            IPackFileService packFileService,
            PackFile bmdPackFile,
            Action<string>? diagnostic = null)
        {
            var bmdPath = packFileService.GetFullPath(bmdPackFile).Replace('\\', '/');
            var tileName = Path.GetFileName(Path.GetDirectoryName(bmdPath));
            if (string.IsNullOrWhiteSpace(tileName))
            {
                diagnostic?.Invoke($"Terrain lookup failed: could not determine tile name from '{bmdPath}'.");
                return null;
            }

            var maxHeight = TryReadPackedMaximumHeight(packFileService, bmdPath) ?? DefaultMaxHeight;
            var packedTiff = FindPackedTiff(packFileService, bmdPath, tileName);
            if (packedTiff != null)
            {
                using var stream = new MemoryStream(packedTiff.Value.Pack.DataSource.ReadData(), false);
                return ReadTiff(stream, packedTiff.Value.FileName, maxHeight);
            }

            var packedHeightMap = TryReadPackedHeightMap(packFileService, bmdPath, diagnostic);
            if (packedHeightMap != null)
                return packedHeightMap;

            foreach (var path in FindAssemblyKitTiffs(bmdPath, tileName))
            {
                using var stream = File.OpenRead(path);
                return ReadTiff(stream, path, maxHeight);
            }

            foreach (var path in FindDevelopmentTiffs(tileName))
            {
                using var stream = File.OpenRead(path);
                return ReadTiff(stream, path, maxHeight);
            }

            return null;
        }

        private static BmdTerrainHeightMap? TryReadPackedHeightMap(
            IPackFileService packFileService,
            string bmdPath,
            Action<string>? diagnostic)
        {
            var directory = Path.GetDirectoryName(bmdPath)?.Replace('\\', '/') ?? string.Empty;
            var packedPath = $"{directory}/tile_height_map.compressed_map";
            var heightFile = packFileService.FindFile(packedPath);
            if (heightFile == null)
            {
                diagnostic?.Invoke($"Packed terrain unavailable: '{packedPath}' was not found.");
                return null;
            }

            try
            {
                var compressedMap = CompressedMapParser.Parse(heightFile.DataSource.ReadData());
                return new BmdTerrainHeightMap
                {
                    Width = compressedMap.Width,
                    Height = compressedMap.Height,
                    Heights = compressedMap.ToFloatSamples(),
                    WorldSize = DefaultWorldSize,
                    SourcePath = $"{packedPath} (decoded {compressedMap.CodecName})"
                };
            }
            catch (Exception ex)
            {
                diagnostic?.Invoke($"Packed terrain decode failed for '{packedPath}': {ex.Message}");
                return null;
            }
        }

        private static (string FileName, PackFile Pack)? FindPackedTiff(IPackFileService packFileService, string bmdPath, string tileName)
        {
            var directory = Path.GetDirectoryName(bmdPath)?.Replace('\\', '/') ?? string.Empty;
            var match = packFileService.FindAllWithExtention(".tif")
                .FirstOrDefault(x =>
                    x.FileName.Replace('\\', '/').StartsWith(directory, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(x.FileName).StartsWith($"{tileName}.height.", StringComparison.OrdinalIgnoreCase));

            return match.Pack == null ? null : match;
        }

        private static IEnumerable<string> FindAssemblyKitTiffs(string bmdPath, string tileName)
        {
            const string terrainMarker = "terrain/";
            var terrainIndex = bmdPath.IndexOf(terrainMarker, StringComparison.OrdinalIgnoreCase);
            if (terrainIndex < 0)
                yield break;

            var relativeDirectory = Path.GetDirectoryName(bmdPath[terrainIndex..]) ?? string.Empty;
            foreach (var drive in DriveInfo.GetDrives().Where(x => x.IsReady))
            {
                var steamRoots = new[]
                {
                    Path.Combine(drive.RootDirectory.FullName, "SteamLibrary"),
                    Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Steam"),
                    Path.Combine(drive.RootDirectory.FullName, "Program Files", "Steam")
                };

                foreach (var steamRoot in steamRoots)
                {
                    var root = Path.Combine(
                        steamRoot, "steamapps", "common", "Total War WARHAMMER III",
                        "assembly_kit", "raw_data", relativeDirectory);

                    if (!Directory.Exists(root))
                        continue;

                    var path = Directory.EnumerateFiles(root, $"{tileName}.height.*.tif").FirstOrDefault();
                    if (path != null)
                        yield return path;
                }
            }
        }

        private static IEnumerable<string> FindDevelopmentTiffs(string tileName)
        {
            foreach (var startPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(startPath);
                while (directory != null)
                {
                    var researchMaps = Path.Combine(directory.FullName, "Research", "maps");
                    if (Directory.Exists(researchMaps))
                    {
                        var match = Directory
                            .EnumerateFiles(researchMaps, $"{tileName}.height.*.tif", SearchOption.AllDirectories)
                            .FirstOrDefault();
                        if (match != null)
                        {
                            yield return match;
                            yield break;
                        }
                    }

                    directory = directory.Parent;
                }
            }
        }

        private static float? TryReadPackedMaximumHeight(IPackFileService packFileService, string bmdPath)
        {
            var directory = Path.GetDirectoryName(bmdPath)?.Replace('\\', '/') ?? string.Empty;
            var heightFile = packFileService.FindFile($"{directory}/tile_height_map.compressed_map");
            if (heightFile == null)
                return null;

            var data = heightFile.DataSource.ReadData();
            if (data.Length < 46 || System.Text.Encoding.ASCII.GetString(data, 0, 8) != "FASTBIN0")
                return null;

            var value = BitConverter.ToSingle(data, 42);
            return float.IsFinite(value) && value > 0f ? value : null;
        }

        private static BmdTerrainHeightMap ReadTiff(Stream stream, string sourcePath, float maxHeight)
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource source = decoder.Frames[0];
            var isFloatHeight = source.Format == PixelFormats.Gray32Float;
            if (!isFloatHeight && source.Format != PixelFormats.Gray16)
                source = new FormatConvertedBitmap(source, PixelFormats.Gray16, null, 0);

            var bytesPerPixel = isFloatHeight ? sizeof(float) : sizeof(ushort);
            var stride = source.PixelWidth * bytesPerPixel;
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);

            var heights = new float[source.PixelWidth, source.PixelHeight];
            for (var y = 0; y < source.PixelHeight; y++)
            {
                for (var x = 0; x < source.PixelWidth; x++)
                {
                    var offset = (y * stride) + (x * bytesPerPixel);
                    heights[x, y] = isFloatHeight
                        ? BitConverter.ToSingle(pixels, offset)
                        : BitConverter.ToUInt16(pixels, offset) / (float)ushort.MaxValue * maxHeight;
                }
            }

            return new BmdTerrainHeightMap
            {
                Width = source.PixelWidth,
                Height = source.PixelHeight,
                Heights = heights,
                WorldSize = DefaultWorldSize,
                SourcePath = sourcePath
            };
        }
    }
}
