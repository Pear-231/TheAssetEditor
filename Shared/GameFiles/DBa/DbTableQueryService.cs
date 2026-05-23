using Shared.ByteParsing;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;

namespace Shared.GameFormats.DB
{
    public interface IDbTableQueryService
    {
        DbTable? LoadTable(string tableName, IPackFileContainer? container = null);
        List<DbTable> LoadTables(string tableName, IPackFileContainer? container = null);
    }

    public class DbTableQueryService(
        IPackFileService packFileService,
        IDbSchemaManager schemaManager,
        ApplicationSettingsService applicationSettingsService) : IDbTableQueryService
    {
        private const string DefaultVanillaDbFileName = "data__";

        private readonly ILogger _logger = Logging.Create<DbTableQueryService>();
        private readonly IPackFileService _packFileService = packFileService;
        private readonly IDbSchemaManager _schemaManager = schemaManager;
        private readonly ApplicationSettingsService _applicationSettingsService = applicationSettingsService;

        public DbTable? LoadTable(string tableName, IPackFileContainer? container = null)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return null;

            if (!_schemaManager.EnsureLoaded())
                return null;

            var sanitisedTableName = tableName.Trim().Trim('"', '\'');
            var normalisedInput = sanitisedTableName.Replace('/', '\\');

            var tableFolderName = GetDbTableFolderNameFromPath(normalisedInput);
            if (tableFolderName != null)
            {
                var file = _packFileService.FindFile(normalisedInput, container);
                if (file == null)
                    return null;

                var data = file.DataSource.ReadData();
                var header = DbTableHeader.ReadData(new ByteChunk(data));

                var schema = _schemaManager.GetSchema(tableFolderName, header.Version);

                if (schema == null)
                {
                    _logger.Here().Warning($"Unable to resolve schema for Db file {normalisedInput} (table folder {tableFolderName}, version {header.Version})");
                    return null;
                }

                return DbTable.CreateFromBytes(data, tableFolderName, schema);
            }

            var tableFolder = DbTableHelpers.NormaliseLookupTableFolder(sanitisedTableName);
            var vanillaFileName = GetVanillaDbFileName(tableFolder);
            var vanillaPath = $"db\\{tableFolder}\\{vanillaFileName}";

            return LoadTable(vanillaPath, container);
        }

        public List<DbTable> LoadTables(string tableName, IPackFileContainer? container = null)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return [];

            if (!_schemaManager.EnsureLoaded())
                return [];

            var sanitisedTableName = tableName.Trim().Trim('"', '\'');
            var tableFolder = DbTableHelpers.NormaliseLookupTableFolder(sanitisedTableName);
            var resolvedPaths = ResolveTableFilePaths(tableFolder, container);
            if (resolvedPaths.Count == 0)
            {
                _logger.Here().Warning($"Unable to resolve Db table folder {tableFolder}.");
                return [];
            }

            var decodedTables = new List<DbTable>(resolvedPaths.Count);
            foreach (var path in resolvedPaths)
            {
                var decodedTable = LoadTable(path, container);
                if (decodedTable != null)
                    decodedTables.Add(decodedTable);
            }

            return decodedTables;
        }

        private List<string> ResolveTableFilePaths(string tableFolderName, IPackFileContainer? container)
        {
            var folderPrefix = $"db\\{tableFolderName}\\";

            var matchingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (container != null)
                AddMatchingPaths(container.GetAllFiles().Keys, folderPrefix, tableFolderName, matchingPaths);
            else
            {
                var allContainers = _packFileService.GetAllPackfileContainers();
                foreach (var currentContainer in allContainers)
                    AddMatchingPaths(currentContainer.GetAllFiles().Keys, folderPrefix, tableFolderName, matchingPaths);
            }

            return matchingPaths
                .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddMatchingPaths(
            IEnumerable<string> files,
            string folderPrefix,
            string tableFolderName,
            HashSet<string> output)
        {
            foreach (var path in files)
            {
                var normalisedPath = path.Replace('/', '\\').Trim();
                if (!normalisedPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var splitPath = normalisedPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (splitPath.Length != 3)
                    continue;

                if (!string.Equals(splitPath[0], "db", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.Equals(splitPath[1], tableFolderName, StringComparison.OrdinalIgnoreCase))
                    continue;

                output.Add(normalisedPath);
            }
        }

        private string GetVanillaDbFileName(string tableFolderName)
        {
            var currentGame = _applicationSettingsService.CurrentSettings.CurrentGame;
            if (currentGame == GameTypeEnum.Warhammer
                || currentGame == GameTypeEnum.Warhammer2
                || currentGame == GameTypeEnum.Warhammer3
                || currentGame == GameTypeEnum.Troy
                || currentGame == GameTypeEnum.ThreeKingdoms
                || currentGame == GameTypeEnum.Pharaoh)
            {
                return DefaultVanillaDbFileName;
            }

            return tableFolderName;
        }

        private static string? GetDbTableFolderNameFromPath(string normalisedPath)
        {
            var splitPath = normalisedPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (splitPath.Length != 3)
                return null;

            if (!string.Equals(splitPath[0], "db", StringComparison.OrdinalIgnoreCase))
                return null;

            return splitPath[1];
        }
    }
}
