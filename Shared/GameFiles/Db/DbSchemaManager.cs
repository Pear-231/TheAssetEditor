using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Serilog;
using Shared.ByteParsing.Parsers;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.Settings;

namespace Shared.GameFormats.Db
{
    public interface IDbSchemaManager
    {
        string? LoadedSchemaPath { get; set; }

        void EnsureLoaded(GameTypeEnum? gameOverride = null);
        void SetSchema(Dictionary<string, List<DbTableSchema>> tableSchemas, string? sourcePath = null);
        DbTableSchema GetSchema(string directory, int tableVersion);
        IReadOnlyList<string> GetSchemaCandidatePaths(GameTypeEnum game);
    }

    public class DbSchemaManager(ApplicationSettingsService applicationSettingsService) : IDbSchemaManager
    {
        private readonly ILogger _logger = Logging.Create<DbSchemaManager>();
        private readonly ApplicationSettingsService _applicationSettingsService = applicationSettingsService;

        private static readonly string[] s_ignoredSchemaFiles =
        [
            "TExc_LocalisableFields.xml",
            "TWaD_relationships.xml"
        ];

        public Dictionary<string, List<DbTableSchema>> TableSchemas { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? LoadedSchemaPath { get; set; }

        public void EnsureLoaded(GameTypeEnum? gameOverride = null)
        {
            if (TableSchemas.Count > 0)
                return;

            var game = gameOverride ?? _applicationSettingsService.CurrentSettings.CurrentGame;
            DirectoryHelper.EnsureCreated(DirectoryHelper.SchemaDirectory);

            // Keep a persisted schema cache and only regenerate when source XML changed
            RefreshCachedSchemaFromAssemblyKit(game);

            foreach (var candidatePath in GetSchemaCandidatePaths(game))
            {
                if (!File.Exists(candidatePath))
                    continue;

                if (LoadFromJsonFile(candidatePath))
                    return;
            }

            var assemblyKitDbDirectory = GetAssemblyKitDbDirectoryForGame(game);
            if (!string.IsNullOrWhiteSpace(assemblyKitDbDirectory) && Directory.Exists(assemblyKitDbDirectory))
            {
                var sourceSchemaFiles = GetAssemblyKitSchemaFilePaths(assemblyKitDbDirectory, s_ignoredSchemaFiles);
                var extractedTableSchemas = CreateFromAssemblyKitXml(sourceSchemaFiles, 2);
                if (extractedTableSchemas != null)
                {
                    SetSchema(extractedTableSchemas, assemblyKitDbDirectory);
                    _logger.Here().Information($"Extracted Db schema from Assembly Kit db directory at {assemblyKitDbDirectory}.");
                    return;
                }
            }

            var message = $"Unable to find or extract a Db schema for game {game} from Assembly Kit db directory {assemblyKitDbDirectory}.";
            _logger.Here().Warning(message);
            throw new InvalidOperationException(message);
        }

        public void SetSchema(Dictionary<string, List<DbTableSchema>> tableSchemas, string? sourcePath = null)
        {
            TableSchemas = ToCaseInsensitiveDictionary(tableSchemas ?? throw new ArgumentNullException(nameof(tableSchemas)));
            LoadedSchemaPath = sourcePath;
        }

        private bool LoadFromJsonFile(string path)
        {
            var tableSchemas = CreateFromJsonFile(path);
            if (tableSchemas != null)
            {
                SetSchema(tableSchemas, path);
                _logger.Here().Information($"Loaded Db schema from {path}.");
                return true;
            }

            _logger.Here().Warning($"Unable to load schema JSON at {path}. JSON format was not recognised.");
            return false;
        }

        public DbTableSchema GetSchema(string directory, int tableVersion)
        {
            EnsureLoaded();

            var schemaKey = directory.Replace("_tables", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!TableSchemas.TryGetValue(schemaKey, out var directorySchemas) || directorySchemas == null || directorySchemas.Count == 0)
                throw new InvalidOperationException($"Unable to resolve Db schema for directory {directory} (schema key {schemaKey}, version {tableVersion}).");

            var byExactVersion = directorySchemas.FirstOrDefault(x => x.Version == tableVersion);
            if (byExactVersion != null)
                return byExactVersion.DeepClone();

            var byClosestLower = directorySchemas
                .Where(x => x.Version <= tableVersion)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();

            if (byClosestLower != null)
                return byClosestLower.DeepClone();

            var fallback = directorySchemas.OrderByDescending(x => x.Version).First();
            return fallback.DeepClone();
        }

        public IReadOnlyList<string> GetSchemaCandidatePaths(GameTypeEnum game)
        {
            var output = new List<string>();
            foreach (var schemaName in GetSchemaNameCandidates(game))
                output.Add(Path.Combine(DirectoryHelper.SchemaDirectory, schemaName));
            return output;
        }

        private void RefreshCachedSchemaFromAssemblyKit(GameTypeEnum game)
        {
            var assemblyKitDbDirectory = GetAssemblyKitDbDirectoryForGame(game);
            if (string.IsNullOrWhiteSpace(assemblyKitDbDirectory) || !Directory.Exists(assemblyKitDbDirectory))
                return;

            var sourceSchemaFiles = GetAssemblyKitSchemaFilePaths(assemblyKitDbDirectory, s_ignoredSchemaFiles);
            if (sourceSchemaFiles.Count == 0)
                return;

            var schemaCandidatePaths = GetSchemaCandidatePaths(game);
            if (schemaCandidatePaths.Count == 0)
                throw new InvalidOperationException($"No schema filename candidates available for game {game}.");

            var schemaPath = schemaCandidatePaths[0];
            var shouldRegenerate = !File.Exists(schemaPath);

            if (!shouldRegenerate)
            {
                var sourceLastWriteTimeUtc = GetLatestAssemblyKitSchemaSourceWriteTimeUtc(sourceSchemaFiles);
                if (sourceLastWriteTimeUtc.HasValue)
                {
                    var cacheLastWriteTimeUtc = File.GetLastWriteTimeUtc(schemaPath);
                    shouldRegenerate = sourceLastWriteTimeUtc.Value > cacheLastWriteTimeUtc;
                }
            }

            if (!shouldRegenerate)
                return;

            var extractedTableSchemas = CreateFromAssemblyKitXml(sourceSchemaFiles, 2);
            if (extractedTableSchemas == null)
                return;

            SetSchema(extractedTableSchemas, assemblyKitDbDirectory);
            WriteDataToJsonFile(schemaPath);
            _logger.Here().Information($"Updated cached Db schema at {schemaPath} from Assembly Kit source {assemblyKitDbDirectory}.");
        }

        private static Dictionary<string, List<DbTableSchema>>? CreateFromAssemblyKitXml(IReadOnlyList<string> assemblyKitSchemaFiles, int rawDbVersion)
        {
            if (assemblyKitSchemaFiles == null || assemblyKitSchemaFiles.Count == 0)
                return null;

            var tableSchemas = new Dictionary<string, List<DbTableSchema>>(StringComparer.OrdinalIgnoreCase);

            foreach (var assemblyKitSchemaFile in assemblyKitSchemaFiles)
            {
                var document = XDocument.Load(assemblyKitSchemaFile);
                var root = document.Root ?? throw new InvalidDataException($"Unable to parse XML root from {assemblyKitSchemaFile}.");

                var directoryFromXml = root.Element("name")?.Value;
                var fileDirectory = Path.GetFileNameWithoutExtension(assemblyKitSchemaFile);
                if (fileDirectory.StartsWith("TWaD_", StringComparison.OrdinalIgnoreCase))
                    fileDirectory = fileDirectory[5..];

                var directory = NormaliseSchemaDirectoryName(directoryFromXml ?? fileDirectory);

                var tableSchema = new DbTableSchema
                {
                    TableName = directory,
                    Version = 0,
                    ColumnSchemas = []
                };

                foreach (var fieldElement in root.Elements().Where(x => x.Name.LocalName.Equals("field", StringComparison.OrdinalIgnoreCase)))
                {
                    var name = GetElementOrAttributeValue(fieldElement, "name");
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var rawType = GetElementOrAttributeValue(fieldElement, "field_type") ?? "text";
                    var required = GetElementOrAttributeValue(fieldElement, "required");
                    var isRequired = ParseRequiredFlag(required);
                    var maxLengthValue = GetElementOrAttributeValue(fieldElement, "max_length");
                    var isFilenameValue = GetElementOrAttributeValue(fieldElement, "is_filename") ?? GetElementOrAttributeValue(fieldElement, "filename");

                    var column = new DbColumnSchema
                    {
                        Name = name,
                        IsKey = (GetElementOrAttributeValue(fieldElement, "primary_key") ?? "0") == "1",
                        IsOptional = !isRequired,
                        IsFileName = ParseFlag(isFilenameValue),
                        FilenameRelativePath = GetElementOrAttributeValue(fieldElement, "filename_relative_path") ?? string.Empty,
                        Description = GetElementOrAttributeValue(fieldElement, "field_description") ?? string.Empty,
                        MaxLength = int.TryParse(maxLengthValue, out var parsedMaxLength) ? parsedMaxLength : 0,
                        Type = MapFieldType(rawType, isRequired, rawDbVersion == 1)
                    };

                    var tableReference = GetElementOrAttributeValue(fieldElement, "column_source_table");
                    if (!string.IsNullOrWhiteSpace(tableReference))
                        column.TableReference = NormaliseSchemaDirectoryName(tableReference);

                    var columnReferences = GetElementOrAttributeValues(fieldElement, "column_source_column");
                    if (columnReferences.Count > 0)
                    {
                        var flattenedColumnReferences = new List<string>();
                        foreach (var columnReference in columnReferences)
                            flattenedColumnReferences.AddRange(SplitReferenceColumns(columnReference));

                        var firstColumn = flattenedColumnReferences.FirstOrDefault();
                        column.FieldReference = firstColumn ?? string.Empty;
                    }

                    tableSchema.ColumnSchemas.Add(column);
                }

                if (!tableSchemas.TryGetValue(tableSchema.TableName, out var schemas))
                {
                    schemas = [];
                    tableSchemas[tableSchema.TableName] = schemas;
                }

                var existing = schemas.FirstOrDefault(x => x.Version == tableSchema.Version);
                if (existing == null)
                    schemas.Add(tableSchema);
                else
                    existing.ColumnSchemas = tableSchema.ColumnSchemas;
            }

            if (tableSchemas.Count == 0)
                return null;

            return tableSchemas;
        }

        private static Dictionary<string, List<DbTableSchema>>? CreateFromJsonFile(string path)
        {
            if (!File.Exists(path))
                return null;

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter());

            var content = File.ReadAllText(path);
            var tableSchemas = JsonSerializer.Deserialize<Dictionary<string, List<DbTableSchema>>>(content, options);

            if (tableSchemas == null || tableSchemas.Count == 0)
                return null;

            return tableSchemas;
        }

        private void WriteDataToJsonFile(string path)
        {
            if (TableSchemas.Count == 0)
                throw new InvalidOperationException("Unable to write Db schema JSON because there are no table schemas to save.");

            DirectoryHelper.EnsureFileFolderCreated(path);

            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());

            var normalisedTableSchemas = ToCaseInsensitiveDictionary(TableSchemas);
            var content = JsonSerializer.Serialize(normalisedTableSchemas, options);
            File.WriteAllText(path, content);
        }

        private static Dictionary<string, List<DbTableSchema>> ToCaseInsensitiveDictionary(Dictionary<string, List<DbTableSchema>> source)
        {
            var output = new Dictionary<string, List<DbTableSchema>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in source)
                output[item.Key] = item.Value?.ToList() ?? [];

            return output;
        }

        private static string NormaliseSchemaDirectoryName(string directory)
        {
            var output = directory.Trim();
            if (output.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                output = output.Replace(".xml", string.Empty, StringComparison.OrdinalIgnoreCase);
            return output;
        }

        private static string? GetElementOrAttributeValue(XElement element, string name)
        {
            var childValue = element
                .Elements()
                .FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (!string.IsNullOrWhiteSpace(childValue))
                return childValue;

            var attributeValue = element
                .Attributes()
                .FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (!string.IsNullOrWhiteSpace(attributeValue))
                return attributeValue;

            return null;
        }

        private static List<string> GetElementOrAttributeValues(XElement element, string name)
        {
            var childValues = element
                .Elements()
                .Where(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (childValues.Count > 0)
                return childValues;

            var attributeValue = element
                .Attributes()
                .FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (string.IsNullOrWhiteSpace(attributeValue))
                return [];

            return [attributeValue];
        }

        private static IEnumerable<string> SplitReferenceColumns(string value)
        {
            return value
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);
        }

        private static bool ParseFlag(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ParseRequiredFlag(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            if (value.Equals("0", StringComparison.OrdinalIgnoreCase)
                || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return true;
        }

        private static DbTypesEnum MapFieldType(string rawType, bool isRequired, bool isOldGame)
        {
            var normalisedType = rawType.Trim().ToLowerInvariant();

            if (normalisedType == "yesno" || normalisedType == "boolean")
                return DbTypesEnum.Boolean;

            if (normalisedType == "single" || normalisedType == "decimal")
                return DbTypesEnum.Single;

            if (normalisedType == "double")
                return DbTypesEnum.Double;

            if (normalisedType == "integer" || normalisedType == "long")
                return DbTypesEnum.Integer;

            if (normalisedType == "autonumber" || normalisedType == "card64" || normalisedType == "longinteger")
                return DbTypesEnum.Int64;

            if (normalisedType == "expression"
                || normalisedType == "text"
                || normalisedType == "memo"
                || normalisedType == "oleobject"
                || normalisedType == "replicationid"
                || normalisedType == "datetime")
            {
                if (isRequired)
                    return isOldGame ? DbTypesEnum.String_ascii : DbTypesEnum.String;
                else
                    return isOldGame ? DbTypesEnum.Optstring_ascii : DbTypesEnum.Optstring;
            }

            if (normalisedType == "colour")
                return DbTypesEnum.Integer;

            if (isRequired)
                return isOldGame ? DbTypesEnum.String_ascii : DbTypesEnum.String;
            else
                return isOldGame ? DbTypesEnum.Optstring_ascii : DbTypesEnum.Optstring;
        }

        private static DateTime? GetLatestAssemblyKitSchemaSourceWriteTimeUtc(IReadOnlyList<string> schemaFiles)
        {
            if (schemaFiles.Count == 0)
                return null;

            var latestWrite = DateTime.MinValue;
            foreach (var schemaFile in schemaFiles)
            {
                var writeTime = File.GetLastWriteTimeUtc(schemaFile);
                if (writeTime > latestWrite)
                    latestWrite = writeTime;
            }

            return latestWrite;
        }

        private static List<string> GetAssemblyKitSchemaFilePaths(string assemblyKitDbDirectory, IReadOnlyList<string>? ignoredSchemaFiles)
        {
            if (string.IsNullOrWhiteSpace(assemblyKitDbDirectory) || !Directory.Exists(assemblyKitDbDirectory))
                return [];

            var ignoredLookup = new HashSet<string>(ignoredSchemaFiles ?? [], StringComparer.OrdinalIgnoreCase);

            return Directory
                .GetFiles(assemblyKitDbDirectory, "TWaD_*.xml", SearchOption.TopDirectoryOnly)
                .Where(path => !ignoredLookup.Contains(Path.GetFileName(path)))
                .ToList();
        }

        private string? GetAssemblyKitDbDirectoryForGame(GameTypeEnum game)
        {
            var configuredGameDataDirectory = _applicationSettingsService.GetGamePathForGame(game);
            if (string.IsNullOrWhiteSpace(configuredGameDataDirectory))
                return null;

            var normalisedConfiguredPath = Path.GetFullPath(configuredGameDataDirectory.TrimEnd('\\', '/'));
            string gameDirectory;

            if (Path.GetFileName(normalisedConfiguredPath).Equals("data", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Directory.GetParent(normalisedConfiguredPath);
                if (parent == null)
                    return null;

                gameDirectory = parent.FullName;
            }
            else if (Directory.Exists(Path.Combine(normalisedConfiguredPath, "data")))
                gameDirectory = normalisedConfiguredPath;
            else
                return null;

            return Path.Combine(gameDirectory, "assembly_kit", "raw_data", "db");
        }

        private static IEnumerable<string> GetSchemaNameCandidates(GameTypeEnum game)
        {
            var id = "wh3";
            if (game == GameTypeEnum.Warhammer)
                id = "wh";
            else if (game == GameTypeEnum.Warhammer2)
                id = "wh2";
            else if (game == GameTypeEnum.Warhammer3)
                id = "wh3";
            else if (game == GameTypeEnum.Troy)
                id = "troy";
            else if (game == GameTypeEnum.ThreeKingdoms)
                id = "3k";
            else if (game == GameTypeEnum.Rome2)
                id = "rom2";
            else if (game == GameTypeEnum.Attila)
                id = "att";
            else if (game == GameTypeEnum.Pharaoh)
                id = "ph";

            return [$"schema_{id}.json", $"{id}_schema.json"];
        }
    }
}
