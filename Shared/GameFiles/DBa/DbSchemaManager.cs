using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Serilog;
using Shared.ByteParsing.Parsers;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.Settings;

namespace Shared.GameFormats.DB
{
    public interface IDbSchemaManager
    {
        bool IsLoaded { get; }
        string? LoadedSchemaPath { get; }

        bool EnsureLoaded(GameTypeEnum? gameOverride = null);
        void SetSchema(DbSchemaFile schemaFile, string? sourcePath = null);
        bool LoadFromJsonFile(string path);
        DbTableSchema? GetSchema(string tableName, int tableVersion);
        IReadOnlyList<string> GetSchemaCandidatePaths(GameTypeEnum game);
    }

    public class DbSchemaManager(ApplicationSettingsService applicationSettingsService) : IDbSchemaManager
    {
        private readonly ILogger _logger = Logging.Create<DbSchemaManager>();
        private readonly ApplicationSettingsService _applicationSettingsService = applicationSettingsService;

        private static readonly string[] s_ignoredDefinitionFiles =
        [
            "TExc_LocalisableFields.xml",
            "TWaD_relationships.xml"
        ];

        private DbSchemaFile? _schema;

        public bool IsLoaded => _schema != null;
        public string? LoadedSchemaPath { get; private set; }

        public bool EnsureLoaded(GameTypeEnum? gameOverride = null)
        {
            if (_schema != null)
                return true;

            var game = gameOverride ?? _applicationSettingsService.CurrentSettings.CurrentGame;
            foreach (var candidatePath in GetSchemaCandidatePaths(game))
            {
                if (!File.Exists(candidatePath))
                    continue;

                if (LoadFromJsonFile(candidatePath))
                    return true;
            }

            var assemblyKitDbDirectory = GetAssemblyKitDbDirectoryForGame(game);
            if (!string.IsNullOrWhiteSpace(assemblyKitDbDirectory) && Directory.Exists(assemblyKitDbDirectory))
            {
                var extractedSchema = ExtractSchemaFromAssemblyKitDbDirectory(assemblyKitDbDirectory);
                if (extractedSchema != null)
                {
                    SetSchema(extractedSchema, assemblyKitDbDirectory);
                    _logger.Here().Information($"Extracted Db schema from Assembly Kit db directory at {assemblyKitDbDirectory}");
                    return true;
                }
            }

            _logger.Here().Warning($"Unable to find or extract a Db schema for game {game}. Checked {GetSchemaCandidatePaths(game).Count} schema path(s). Assembly Kit db directory: {assemblyKitDbDirectory ?? "not derivable from settings"}");
            return false;
        }

        public void SetSchema(DbSchemaFile schemaFile, string? sourcePath = null)
        {
            _schema = schemaFile ?? throw new ArgumentNullException(nameof(schemaFile));
            LoadedSchemaPath = sourcePath;
        }

        public bool LoadFromJsonFile(string path)
        {
            if (!File.Exists(path))
                return false;

            var content = File.ReadAllText(path);

            var options = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter());

            var schema = JsonSerializer.Deserialize<DbSchemaFile>(content, options);
            if (schema != null && schema.TableSchemas.Count > 0)
            {
                SetSchema(schema, path);
                _logger.Here().Information($"Loaded Db schema from {path}");
                return true;
            }

            _logger.Here().Warning($"Unable to load schema JSON at {path}. JSON format was not recognised.");
            return false;
        }

        public DbTableSchema? GetSchema(string tableName, int tableVersion)
        {
            if (_schema == null)
                return null;

            var lookupTableName = DbTableHelpers.NormaliseLookupTableFolder(tableName);
            if (!_schema.TableSchemas.TryGetValue(lookupTableName, out var dbTableSchemas) || dbTableSchemas.Count == 0)
                return null;

            var byExactVersion = dbTableSchemas.FirstOrDefault(x => x.Version == tableVersion);
            if (byExactVersion != null)
                return byExactVersion.DeepClone();

            var byClosestLower = dbTableSchemas
                .Where(x => x.Version <= tableVersion)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();

            if (byClosestLower != null)
                return byClosestLower.DeepClone();

            var fallback = dbTableSchemas.OrderByDescending(x => x.Version).First();
            return fallback.DeepClone();

        }

        public IReadOnlyList<string> GetSchemaCandidatePaths(GameTypeEnum game)
        {
            var output = new List<string>();
            foreach (var schemaName in GetSchemaNameCandidates(game))
                output.Add(Path.Combine(DirectoryHelper.SchemaDirectory, schemaName));

            return output;
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
            {
                gameDirectory = normalisedConfiguredPath;
            }
            else
            {
                return null;
            }

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

        private static DbSchemaFile? ExtractSchemaFromAssemblyKitDbDirectory(string assemblyKitDbDirectory)
        {
            if (string.IsNullOrWhiteSpace(assemblyKitDbDirectory))
                return null;
            if (!Directory.Exists(assemblyKitDbDirectory))
                return null;

            var schemaFile = new DbSchemaFile();

            var assemblyKitSchemaFiles = Directory
                .GetFiles(assemblyKitDbDirectory, "TWaD_*.xml", SearchOption.TopDirectoryOnly)
                .Where(path => !s_ignoredDefinitionFiles.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (var assemblyKitSchemaFile in assemblyKitSchemaFiles)
            {
                var tableSchema = ParseSchemaFile(assemblyKitSchemaFile, 2);

                if (!schemaFile.TableSchemas.TryGetValue(tableSchema.TableName, out var schemas))
                {
                    schemas = [];
                    schemaFile.TableSchemas[tableSchema.TableName] = schemas;
                }

                var existing = schemas.FirstOrDefault(x => x.Version == tableSchema.Version);
                if (existing == null)
                    schemas.Add(tableSchema);
                else
                    existing.ColumnSchemas = tableSchema.ColumnSchemas;
            }

            if (schemaFile.TableSchemas.Count == 0)
                return null;

            return schemaFile;
        }

        private static DbTableSchema ParseSchemaFile(string filePath, int rawDbVersion)
        {
            var document = XDocument.Load(filePath);
            var root = document.Root ?? throw new InvalidDataException($"Unable to parse XML root from {filePath}");

            var tableNameFromXml = root.Element("name")?.Value;
            var fileTableName = Path.GetFileNameWithoutExtension(filePath);
            if (fileTableName.StartsWith("TWaD_", StringComparison.OrdinalIgnoreCase))
                fileTableName = fileTableName[5..];

            var tableName = DbTableHelpers.NormaliseSchemaTableName(tableNameFromXml ?? fileTableName);

            var schema = new DbTableSchema
            {
                TableName = tableName,
                Version = 0,
                ColumnSchemas = []
            };

            foreach (var fieldElement in root.Elements("field"))
            {
                var name = fieldElement.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var rawType = fieldElement.Attribute("field_type")?.Value ?? "text";
                var required = fieldElement.Attribute("required")?.Value ?? "1";
                var isRequired = required == "1";

                var column = new DbColumnSchema
                {
                    Name = name,
                    IsKey = fieldElement.Attribute("primary_key")?.Value == "1",
                    IsOptional = !isRequired,
                    IsFileName = ParseFlag(fieldElement.Attribute("filename")?.Value),
                    FilenameRelativePath = fieldElement.Attribute("filename_relative_path")?.Value ?? string.Empty,
                    Description = fieldElement.Attribute("field_description")?.Value ?? string.Empty,
                    MaxLength = ParseInt(fieldElement.Attribute("max_length")?.Value),
                    Type = MapFieldType(rawType, isRequired, rawDbVersion == 1)
                };

                var tableReference = fieldElement.Attribute("column_source_table")?.Value;
                if (!string.IsNullOrWhiteSpace(tableReference))
                    column.TableReference = DbTableHelpers.NormaliseSchemaTableName(tableReference);

                var columnReference = fieldElement.Attribute("column_source_column")?.Value;
                if (!string.IsNullOrWhiteSpace(columnReference))
                {
                    var firstColumn = SplitReferenceColumns(columnReference).FirstOrDefault();
                    column.FieldReference = firstColumn ?? string.Empty;
                }

                schema.ColumnSchemas.Add(column);
            }

            return schema;
        }

        private static IEnumerable<string> SplitReferenceColumns(string value)
        {
            return value
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);
        }

        private static int ParseInt(string? value)
        {
            return int.TryParse(value, out var parsed) ? parsed : 0;
        }

        private static bool ParseFlag(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
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
                    return (isOldGame ? DbTypesEnum.String_ascii : DbTypesEnum.String);
                else
                    return (isOldGame ? DbTypesEnum.Optstring_ascii : DbTypesEnum.Optstring);
            }

            if (normalisedType == "colour")
                return DbTypesEnum.Integer;

            if (isRequired)
                return (isOldGame ? DbTypesEnum.String_ascii : DbTypesEnum.String);
            else
                return (isOldGame ? DbTypesEnum.Optstring_ascii : DbTypesEnum.Optstring);
        }
    }
}
