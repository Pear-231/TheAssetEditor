using System.Text;
using Newtonsoft.Json;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace Editors.Reports.Files
{
    public class GenerateGamePacksReportCommand(GamePacksReportGenerator generator, ApplicationSettingsService settingsService) : IAeCommand
    {
        public void Execute() => generator.Create(settingsService.CurrentSettings.CurrentGame);
    }

    public class GamePacksReportGenerator(IPackFileContainerLoader containerLoader)
    {
        private readonly ILogger _logger = Logging.Create<GamePacksReportGenerator>();
        private readonly IPackFileContainerLoader _containerLoader = containerLoader;

        public string Create(GameTypeEnum game)
        {
            var gameName = GameInformationDatabase.GetGameById(game).DisplayName;
            var container = _containerLoader.CreateFromGameEnum(PackFileContainerType.Database, game);
            if (container == null)
                throw new InvalidOperationException($"Unable to load pack files for {gameName} because no game directory is configured.");

            var outputFolder = Path.Combine(DirectoryHelper.ReportsDirectory, "GamePacks");
            DirectoryHelper.EnsureCreated(outputFolder);
            var timeStamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            var outputJsonPath = Path.Combine(outputFolder, $"{gameName}_{timeStamp}.json");

            _logger.Here().Information("Creating game packs report for {Game}. Result will be saved at {OutputPath}.", gameName, outputJsonPath);

            var result = BuildResult(gameName, container);

            File.WriteAllText(outputJsonPath, JsonConvert.SerializeObject(result, Formatting.Indented), new UTF8Encoding(false));

            return outputJsonPath;
        }

        private static GameResult BuildResult(string gameName, IPackFileContainer container)
        {
            var packedFiles = container.GetAllFiles()
                .Where(x => x.Value.DataSource is PackedFileSource)
                .Select(x => (Path: x.Key, Source: (PackedFileSource)x.Value.DataSource));

            var packResults = packedFiles
                .GroupBy(x => Path.GetFileName(x.Source.Parent.FilePath), StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildPackResult(group.Key, group))
                .OrderBy(x => x.Pack, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new GameResult(gameName, packResults);
        }

        private static PackResult BuildPackResult(string packName, IEnumerable<(string Path, PackedFileSource Source)> files)
        {
            var fileList = files.ToList();
            var firstSource = fileList[0].Source;
            var packResult = new PackResult(packName, ReadPackHeader(firstSource.Parent.FilePath))
            {
                Files = fileList.Count
            };

            var encryptedByExtension = new Dictionary<string, FileTypeResult>(StringComparer.OrdinalIgnoreCase);
            var compressedByExtension = new Dictionary<string, CompressedFileTypeResult>(StringComparer.OrdinalIgnoreCase);
            var compressionFormatsByExtension = new Dictionary<string, Dictionary<string, CompressionFormatResult>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (internalPath, source) in fileList)
            {
                var extension = Path.GetExtension(internalPath);
                if (string.IsNullOrEmpty(extension))
                    extension = "(none)";

                if (source.IsCompressed)
                {
                    var compressedResult = GetOrAdd(compressedByExtension, extension, () => new CompressedFileTypeResult());
                    compressedResult.Count++;

                    var formats = GetOrAdd(compressionFormatsByExtension, extension, () => new Dictionary<string, CompressionFormatResult>(StringComparer.OrdinalIgnoreCase));
                    var formatResult = GetOrAdd(formats, source.CompressionFormat.ToString(), () => new CompressionFormatResult());
                    formatResult.Count++;
                    formatResult.FilePaths.Add(internalPath);
                }

                if (!source.IsEncrypted)
                    continue;

                var encryptedResult = GetOrAdd(encryptedByExtension, extension, () => new FileTypeResult());
                encryptedResult.Count++;
                encryptedResult.FilePaths.Add(internalPath);

                try
                {
                    var bytes = source.ReadData();
                    FileEncryption.TryValidateDecryptedContent(internalPath, bytes, out var error);
                    if (error != null)
                        encryptedResult.Failures.Add($"{internalPath}: {error}");
                }
                catch (Exception exception)
                {
                    encryptedResult.Failures.Add($"{internalPath}: {exception.GetType().Name}: {exception.Message} " +
                        $"(compressed={source.IsCompressed}, compression={source.CompressionFormat}, stored-size={source.Size}, uncompressed-size={source.UncompressedSize})");
                }
            }

            packResult.EncryptedFiles = encryptedByExtension
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

            packResult.CompressedFiles = compressedByExtension
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

            foreach (var (extension, compressedResult) in packResult.CompressedFiles)
                compressedResult.CompressionFormats = compressionFormatsByExtension[extension]
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

            return packResult;
        }

        private static PackHeaderInfo ReadPackHeader(string filePath)
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);
            var header = PackFileSerializerLoader.ReadHeader(reader);

            return new PackHeaderInfo(
                header.Version.ToString(),
                header.ByteMask,
                header.PackFileType.ToString(),
                header.HasExtendedHeader,
                header.HasEncryptedData,
                header.HasEncryptedIndex,
                header.HasIndexWithTimeStamp,
                header.ReferenceFileCount,
                header.FileCount);
        }

        private static T GetOrAdd<T>(Dictionary<string, T> map, string key, Func<T> factory)
        {
            if (!map.TryGetValue(key, out var value))
            {
                value = factory();
                map[key] = value;
            }
            return value;
        }

        public sealed record GameResult(string Game, IReadOnlyList<PackResult> Packs);
        public sealed record PackHeaderInfo(
            string PfhVersion,
            int ByteMask,
            string PackFileType,
            bool HasExtendedHeader,
            bool HasEncryptedData,
            bool HasEncryptedIndex,
            bool HasIndexWithTimeStamp,
            uint ReferenceFileCount,
            uint FileCount);

        public sealed class PackResult(string pack, PackHeaderInfo header)
        {
            public string Pack { get; } = pack;
            public PackHeaderInfo Header { get; } = header;
            public int Files { get; set; }
            public Dictionary<string, FileTypeResult> EncryptedFiles { get; set; } = [];
            public Dictionary<string, CompressedFileTypeResult> CompressedFiles { get; set; } = [];
        }

        public sealed class FileTypeResult
        {
            public int Count { get; set; }
            public List<string> FilePaths { get; } = [];
            public List<string> Failures { get; } = [];
        }

        public sealed class CompressedFileTypeResult
        {
            public int Count { get; set; }
            public Dictionary<string, CompressionFormatResult> CompressionFormats { get; set; } = [];
        }

        public sealed class CompressionFormatResult
        {
            public int Count { get; set; }
            public List<string> FilePaths { get; } = [];
        }
    }
}
