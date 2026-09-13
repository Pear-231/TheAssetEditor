using System.Text;
using Newtonsoft.Json;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace Editors.Reports.Audio
{
    public class GenerateCompressionAndEncryptionReportCommand(
        CompressionAndEncryptionReportGenerator generator,
        ApplicationSettingsService settingsService) : IAeCommand
    {
        public void Execute() => generator.Create(settingsService.CurrentSettings.CurrentGame);
    }

    public class CompressionAndEncryptionReportGenerator(IPackFileContainerLoader containerLoader)
    {
        private readonly ILogger _logger = Logging.Create<CompressionAndEncryptionReportGenerator>();
        private readonly IPackFileContainerLoader _containerLoader = containerLoader;

        public string Create(GameTypeEnum game)
        {
            var gameName = GameInformationDatabase.GetGameById(game).DisplayName;
            var container = _containerLoader.CreateFromGameEnum(PackFileContainerType.Database, game);
            if (container == null)
                throw new InvalidOperationException($"Unable to load pack files for {gameName} because no game directory is configured.");

            var outputFolder = Path.Combine(DirectoryHelper.ReportsDirectory, "CompressionAndEncryption");
            DirectoryHelper.EnsureCreated(outputFolder);
            var timeStamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            var outputJsonPath = Path.Combine(outputFolder, $"{gameName}_{timeStamp}.json");

            _logger.Here().Information("Creating compression and encryption report for {Game}. Result will be saved at {OutputPath}.", gameName, outputJsonPath);

            var result = BuildResult(gameName, container);

            File.WriteAllText(outputJsonPath, JsonConvert.SerializeObject(result, Formatting.Indented), new UTF8Encoding(false));

            return outputJsonPath;
        }

        private static GameResult BuildResult(string gameName, IPackFileContainer container)
        {
            var packedFiles = new List<(string Path, PackedFileSource Source)>();
            var nonPackedFiles = 0;

            foreach (var (path, file) in container.GetAllFiles())
            {
                if (file.DataSource is PackedFileSource source)
                    packedFiles.Add((path, source));
                else
                    nonPackedFiles++;
            }

            var packResults = packedFiles
                .GroupBy(x => Path.GetFileName(x.Source.Parent.FilePath), StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildPackResult(group.Key, group))
                .OrderBy(x => x.Pack, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new GameResult(gameName, nonPackedFiles, packResults);
        }

        private static PackResult BuildPackResult(string packName, IEnumerable<(string Path, PackedFileSource Source)> files)
        {
            var fileList = files.ToList();
            var firstSource = fileList[0].Source;
            var packResult = new PackResult(packName, ReadPackVersion(firstSource.Parent.FilePath).ToString(), firstSource.IsEncrypted)
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
                    var compressedResult = GetOrAdd(compressedByExtension, extension, e => new CompressedFileTypeResult(e));
                    compressedResult.Count++;

                    var formats = GetOrAdd(compressionFormatsByExtension, extension, _ => new Dictionary<string, CompressionFormatResult>(StringComparer.OrdinalIgnoreCase));
                    var formatResult = GetOrAdd(formats, source.CompressionFormat.ToString(), format => new CompressionFormatResult(format));
                    formatResult.Count++;
                    formatResult.FilePaths.Add(internalPath);
                }

                if (!source.IsEncrypted)
                    continue;

                var encryptedResult = GetOrAdd(encryptedByExtension, extension, e => new FileTypeResult(e));
                encryptedResult.Count++;
                encryptedResult.FilePaths.Add(internalPath);

                try
                {
                    var bytes = source.ReadData();
                    packResult.DecryptedBytes += bytes.LongLength;
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

            packResult.EncryptedFiles = encryptedByExtension.Values.OrderBy(x => x.Extension, StringComparer.OrdinalIgnoreCase).ToList();

            var compressedResults = compressedByExtension.Values.OrderBy(x => x.Extension, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var compressedResult in compressedResults)
                compressedResult.CompressionFormats = compressionFormatsByExtension[compressedResult.Extension].Values.OrderBy(x => x.Format, StringComparer.OrdinalIgnoreCase).ToList();
            packResult.CompressedFiles = compressedResults;

            return packResult;
        }

        private static PackFileVersion ReadPackVersion(string filePath)
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);
            return PackFileSerializerLoader.ReadHeader(reader).Version;
        }

        private static T GetOrAdd<T>(Dictionary<string, T> map, string key, Func<string, T> factory)
        {
            if (!map.TryGetValue(key, out var value))
            {
                value = factory(key);
                map[key] = value;
            }
            return value;
        }

        public sealed record GameResult(string Game, int NonPackedFiles, IReadOnlyList<PackResult> Packs);
        public sealed class PackResult(string pack, string pfhVersion, bool isEncrypted)
        {
            public string Pack { get; } = pack;
            public string PfhVersion { get; } = pfhVersion;
            public bool IsEncrypted { get; } = isEncrypted;
            public int Files { get; set; }
            public long DecryptedBytes { get; set; }
            public List<FileTypeResult> EncryptedFiles { get; set; } = [];
            public List<CompressedFileTypeResult> CompressedFiles { get; set; } = [];
        }

        public sealed class FileTypeResult(string extension)
        {
            public string Extension { get; } = extension;
            public int Count { get; set; }
            public List<string> FilePaths { get; } = [];
            public List<string> Failures { get; } = [];
        }

        public sealed class CompressedFileTypeResult(string extension)
        {
            public string Extension { get; } = extension;
            public int Count { get; set; }
            public List<CompressionFormatResult> CompressionFormats { get; set; } = [];
        }

        public sealed class CompressionFormatResult(string format)
        {
            public string Format { get; } = format;
            public int Count { get; set; }
            public List<string> FilePaths { get; } = [];
        }
    }
}
