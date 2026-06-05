using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Xml.Linq;
using Serilog;
using Shared.ByteParsing;
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
        bool TryInferSchemaFromAssemblyKitData(string directory, int tableVersion, byte[] tableData, DbTableSchema schema, out DbTableSchema refinedSchema);
        bool TryGetLocatedAssemblyKitRowOffsets(string directory, int tableVersion, byte[] tableData, DbTableSchema schema, out List<(string Key, int RowOffset)> locatedRows);
        IReadOnlyList<string> GetSchemaCandidatePaths(GameTypeEnum game);
    }

    public class DbSchemaManager(ApplicationSettingsService applicationSettingsService) : IDbSchemaManager
    {
        private const int MaxInferenceDeadStates = 120000;

        private readonly ILogger _logger = Logging.Create<DbSchemaManager>();
        private readonly ApplicationSettingsService _applicationSettingsService = applicationSettingsService;

        private static readonly string[] s_ignoredSchemaFiles =
        [
            "TExc_LocalisableFields.xml",
            "TWaD_relationships.xml"
        ];

        public Dictionary<string, List<DbTableSchema>> TableSchemas { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? LoadedSchemaPath { get; set; }
        private Dictionary<string, List<DbTableSchemaOverride>> _schemaOverrides = new(StringComparer.OrdinalIgnoreCase);

        private class DbTableSchemaOverride
        {
            public int Version { get; set; }
            public List<string> RemovedColumns { get; set; } = [];
            public List<string> OrderedColumns { get; set; } = [];
            public Dictionary<string, DbTypesEnum> TypeOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, DbStringSerialisationMode> StringSerialisationOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class InferenceRowCandidate
        {
            public required string Key { get; init; }
            public required Dictionary<string, string> Values { get; init; }
            public required int Score { get; init; }
        }

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

                if (LoadFromJsonFile(candidatePath, game))
                    return;
            }

            var assemblyKitDbDirectory = GetAssemblyKitDbDirectoryForGame(game);
            if (!string.IsNullOrWhiteSpace(assemblyKitDbDirectory) && Directory.Exists(assemblyKitDbDirectory))
            {
                var sourceSchemaFiles = GetAssemblyKitSchemaFilePaths(assemblyKitDbDirectory, s_ignoredSchemaFiles);
                var extractedTableSchemas = CreateFromAssemblyKitXml(sourceSchemaFiles, 2, assemblyKitDbDirectory);
                if (extractedTableSchemas != null)
                {
                    SetSchema(extractedTableSchemas, assemblyKitDbDirectory);
                    LoadSchemaOverrides(game);
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

        private bool LoadFromJsonFile(string path, GameTypeEnum game)
        {
            var tableSchemas = CreateFromJsonFile(path);
            if (tableSchemas != null)
            {
                SetSchema(tableSchemas, path);
                LoadSchemaOverrides(game);
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
            var baseSchema = ResolveBaseSchema(schemaKey, directory, tableVersion);
            return ApplySchemaOverride(schemaKey, tableVersion, baseSchema);
        }

        public bool TryInferSchemaFromAssemblyKitData(string directory, int tableVersion, byte[] tableData, DbTableSchema schema, out DbTableSchema refinedSchema)
        {
            refinedSchema = schema;

            var assemblyKitDbDirectory = GetAssemblyKitDbDirectoryForGame(_applicationSettingsService.CurrentSettings.CurrentGame);
            if (string.IsNullOrWhiteSpace(assemblyKitDbDirectory) || !Directory.Exists(assemblyKitDbDirectory))
                return false;

            var schemaKey = directory.Replace("_tables", string.Empty, StringComparison.OrdinalIgnoreCase);
            var dataXmlPath = Path.Combine(assemblyKitDbDirectory, $"{schemaKey}.xml");
            if (!File.Exists(dataXmlPath))
                return false;

            var rowCandidates = LoadInferenceRowCandidates(dataXmlPath, schemaKey);
            if (rowCandidates.Count == 0)
                return false;

            var failureReasons = new List<string>();
            var locatedCandidates = new List<(InferenceRowCandidate Candidate, int RowOffset)>();

            foreach (var candidate in rowCandidates)
            {
                if (!TryFindRowOffset(tableData, schema, candidate.Key, out var rowOffset))
                {
                    failureReasons.Add($"Could not locate row key '{candidate.Key}' in binary data.");
                    continue;
                }

                locatedCandidates.Add((candidate, rowOffset));
            }

            foreach (var located in locatedCandidates)
            {
                var extraAnchors = locatedCandidates
                    .Where(x => !x.Candidate.Key.Equals(located.Candidate.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(x =>
                    {
                        var enrichedRow = BuildInferenceAnchorSubset(x.Candidate.Values);
                        enrichedRow["key"] = x.Candidate.Key;
                        return (ExpectedRow: (IReadOnlyDictionary<string, string>)enrichedRow, RowOffset: x.RowOffset);
                    })
                    .ToList();

                var workingSchema = schema.DeepClone();
                if (!TryInferSchemaForCandidate(workingSchema, tableData, located.Candidate.Key, located.Candidate.Values, located.RowOffset, extraAnchors, out var failureReason))
                {
                    failureReasons.Add($"Candidate '{located.Candidate.Key}' at offset {located.RowOffset} failed: {failureReason}");
                    continue;
                }

                refinedSchema = workingSchema;
                return true;
            }

            throw new InvalidOperationException($"Unable to infer a Db schema for {directory} version {tableVersion}. {string.Join(" | ", failureReasons.Take(6))}");
        }

        public bool TryGetLocatedAssemblyKitRowOffsets(string directory, int tableVersion, byte[] tableData, DbTableSchema schema, out List<(string Key, int RowOffset)> locatedRows)
        {
            locatedRows = [];

            var assemblyKitDbDirectory = GetAssemblyKitDbDirectoryForGame(_applicationSettingsService.CurrentSettings.CurrentGame);
            if (string.IsNullOrWhiteSpace(assemblyKitDbDirectory) || !Directory.Exists(assemblyKitDbDirectory))
                return false;

            var schemaKey = directory.Replace("_tables", string.Empty, StringComparison.OrdinalIgnoreCase);
            var dataXmlPath = Path.Combine(assemblyKitDbDirectory, $"{schemaKey}.xml");
            if (!File.Exists(dataXmlPath))
                return false;

            var rowCandidates = LoadInferenceRowCandidates(dataXmlPath, schemaKey);
            if (rowCandidates.Count == 0)
                return false;

            foreach (var candidate in rowCandidates)
            {
                if (!TryFindRowOffset(tableData, schema, candidate.Key, out var rowOffset))
                    continue;

                locatedRows.Add((candidate.Key, rowOffset));
            }

            return locatedRows.Count > 0;
        }

        private static Dictionary<string, string> BuildInferenceAnchorSubset(IReadOnlyDictionary<string, string> values)
        {
            var subset = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in values)
            {
                if (!string.IsNullOrEmpty(value))
                    subset[key] = value;
            }
            return subset;
        }

        private static Dictionary<string, string> BuildInferenceSolverGuidanceRow(IReadOnlyDictionary<string, string> values)
        {
            var guidance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in values)
            {
                // Zero-valued numerics are excluded: they can coincidentally match 4 zero bytes
                // that appear when consecutive empty-string columns are read as a single integer.
                // Empty strings are kept: they represent real binary column values and must generate Move candidates.
                if (!string.IsNullOrEmpty(value)
                    && double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var numericValue)
                    && numericValue == 0.0)
                    continue;

                guidance[key] = value;
            }
            return guidance;
        }

        private DbTableSchema ResolveBaseSchema(string schemaKey, string directory, int tableVersion)
        {
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

        private DbTableSchema ApplySchemaOverride(string schemaKey, int tableVersion, DbTableSchema schema)
        {
            if (!_schemaOverrides.TryGetValue(schemaKey, out var tableOverrides) || tableOverrides.Count == 0)
                return schema;

            var schemaOverride = tableOverrides.FirstOrDefault(x => x.Version == tableVersion)
                ?? tableOverrides.Where(x => x.Version <= tableVersion).OrderByDescending(x => x.Version).FirstOrDefault();

            if (schemaOverride == null)
                return schema;

            var removedColumns = new HashSet<string>(schemaOverride.RemovedColumns ?? [], StringComparer.OrdinalIgnoreCase);
            if (removedColumns.Count > 0)
            {
                schema.ColumnSchemas = schema.ColumnSchemas
                    .Where(column => !removedColumns.Contains(column.Name))
                    .ToList();
            }

            if (schemaOverride.OrderedColumns != null && schemaOverride.OrderedColumns.Count > 0)
                schema.ColumnSchemas = OrderColumnsByObservedData(schema.ColumnSchemas, schemaOverride.OrderedColumns);

            if (schemaOverride.TypeOverrides != null && schemaOverride.TypeOverrides.Count > 0)
            {
                foreach (var column in schema.ColumnSchemas)
                {
                    if (!schemaOverride.TypeOverrides.TryGetValue(column.Name, out var typeOverride))
                        continue;

                    column.Type = typeOverride;
                }
            }

            if (schemaOverride.StringSerialisationOverrides != null && schemaOverride.StringSerialisationOverrides.Count > 0)
            {
                foreach (var column in schema.ColumnSchemas)
                {
                    if (!schemaOverride.StringSerialisationOverrides.TryGetValue(column.Name, out var serialisationModeOverride))
                        continue;

                    column.StringSerialisationMode = serialisationModeOverride;
                }
            }

            return schema;
        }

        private void LoadSchemaOverrides(GameTypeEnum game)
        {
            _schemaOverrides = new Dictionary<string, List<DbTableSchemaOverride>>(StringComparer.OrdinalIgnoreCase);

            foreach (var schemaOverridePath in GetSchemaOverrideCandidatePaths(game))
            {
                if (!File.Exists(schemaOverridePath))
                    continue;

                var loadedOverrides = CreateSchemaOverridesFromJsonFile(schemaOverridePath);
                if (loadedOverrides == null)
                    continue;

                _schemaOverrides = loadedOverrides;
                _logger.Here().Information($"Loaded Db schema overrides from {schemaOverridePath}.");
                return;
            }
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

            var extractedTableSchemas = CreateFromAssemblyKitXml(sourceSchemaFiles, 2, assemblyKitDbDirectory);
            if (extractedTableSchemas == null)
                return;

            SetSchema(extractedTableSchemas, assemblyKitDbDirectory);
            WriteDataToJsonFile(schemaPath);
            _logger.Here().Information($"Updated cached Db schema at {schemaPath} from Assembly Kit source {assemblyKitDbDirectory}.");
        }

        private static Dictionary<string, List<DbTableSchema>>? CreateFromAssemblyKitXml(IReadOnlyList<string> assemblyKitSchemaFiles, int rawDbVersion, string? assemblyKitDbDirectory)
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

            ApplyAssemblyKitDataRefinements(tableSchemas, assemblyKitDbDirectory);

            return tableSchemas;
        }

        private static void ApplyAssemblyKitDataRefinements(Dictionary<string, List<DbTableSchema>> tableSchemas, string? assemblyKitDbDirectory)
        {
            if (string.IsNullOrWhiteSpace(assemblyKitDbDirectory) || !Directory.Exists(assemblyKitDbDirectory))
                return;

            var localisableFieldsByTable = LoadLocalisableFieldsByTable(assemblyKitDbDirectory);
            var observedFieldsByTable = LoadObservedFieldsByTable(assemblyKitDbDirectory);

            foreach (var tableEntry in tableSchemas)
            {
                var tableName = tableEntry.Key;
                var tableSchemasForName = tableEntry.Value;

                localisableFieldsByTable.TryGetValue(tableName, out var localisableFields);
                observedFieldsByTable.TryGetValue(tableName, out var observedFieldsInOrder);

                foreach (var schema in tableSchemasForName)
                {
                    var schemaColumns = schema.ColumnSchemas;
                    var columnsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (localisableFields != null && localisableFields.Count > 0)
                    {
                        foreach (var field in localisableFields)
                            columnsToRemove.Add(field);
                    }

                    if (columnsToRemove.Count > 0)
                    {
                        schemaColumns = schemaColumns
                            .Where(column => !columnsToRemove.Contains(column.Name))
                            .ToList();
                    }

                    if (observedFieldsInOrder != null && observedFieldsInOrder.Count > 0)
                        schemaColumns = OrderColumnsByObservedData(schemaColumns, observedFieldsInOrder);

                    schema.ColumnSchemas = schemaColumns;
                }
            }
        }

        private static List<DbColumnSchema> OrderColumnsByObservedData(List<DbColumnSchema> columns, IReadOnlyList<string> observedFieldsInOrder)
        {
            var columnByName = new Dictionary<string, DbColumnSchema>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                if (!columnByName.ContainsKey(column.Name))
                    columnByName[column.Name] = column;
            }

            var orderedColumns = new List<DbColumnSchema>();
            var addedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fieldName in observedFieldsInOrder)
            {
                if (!columnByName.TryGetValue(fieldName, out var column))
                    continue;

                if (addedColumns.Contains(column.Name))
                    continue;

                orderedColumns.Add(column);
                addedColumns.Add(column.Name);
            }

            foreach (var column in columns)
            {
                if (addedColumns.Contains(column.Name))
                    continue;

                orderedColumns.Add(column);
            }

            return orderedColumns;
        }

        private static Dictionary<string, HashSet<string>> LoadLocalisableFieldsByTable(string assemblyKitDbDirectory)
        {
            var output = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var localisableFieldsPath = Path.Combine(assemblyKitDbDirectory, "TExc_LocalisableFields.xml");
            if (!File.Exists(localisableFieldsPath))
                return output;

            var document = XDocument.Load(localisableFieldsPath);
            var root = document.Root;
            if (root == null)
                return output;

            foreach (var entry in root.Elements().Where(x => x.Name.LocalName.Equals("TExc_LocalisableFields", StringComparison.OrdinalIgnoreCase)))
            {
                var tableName = GetElementOrAttributeValue(entry, "table_name");
                var fieldName = GetElementOrAttributeValue(entry, "field");
                if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(fieldName))
                    continue;

                var normalisedTableName = NormaliseSchemaDirectoryName(tableName);
                if (!output.TryGetValue(normalisedTableName, out var fields))
                {
                    fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    output[normalisedTableName] = fields;
                }

                fields.Add(fieldName);
            }

            return output;
        }

        private static Dictionary<string, List<string>> LoadObservedFieldsByTable(string assemblyKitDbDirectory)
        {
            var output = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var dataFiles = Directory
                .GetFiles(assemblyKitDbDirectory, "*.xml", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var fileName = Path.GetFileName(path);
                    if (fileName.StartsWith("TWaD_", StringComparison.OrdinalIgnoreCase))
                        return false;

                    if (fileName.StartsWith("TExc_", StringComparison.OrdinalIgnoreCase))
                        return false;

                    return true;
                });

            foreach (var dataFilePath in dataFiles)
            {
                var tableName = NormaliseSchemaDirectoryName(Path.GetFileNameWithoutExtension(dataFilePath));
                if (string.IsNullOrWhiteSpace(tableName))
                    continue;

                var document = XDocument.Load(dataFilePath);
                var root = document.Root;
                if (root == null)
                    continue;

                var observedFields = new List<string>();
                var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var rowElement in root.Elements().Where(x => x.Name.LocalName.Equals(tableName, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var fieldElement in rowElement.Elements())
                    {
                        var fieldName = fieldElement.Name.LocalName;
                        if (seenFields.Contains(fieldName))
                            continue;

                        seenFields.Add(fieldName);
                        observedFields.Add(fieldName);
                    }
                }

                if (observedFields.Count == 0)
                    continue;

                output[tableName] = observedFields;
            }

            return output;
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

        private static Dictionary<string, List<DbTableSchemaOverride>>? CreateSchemaOverridesFromJsonFile(string path)
        {
            if (!File.Exists(path))
                return null;

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter());

            var content = File.ReadAllText(path);
            var schemaOverrides = JsonSerializer.Deserialize<Dictionary<string, List<DbTableSchemaOverride>>>(content, options);
            if (schemaOverrides == null || schemaOverrides.Count == 0)
                return null;

            var output = new Dictionary<string, List<DbTableSchemaOverride>>(StringComparer.OrdinalIgnoreCase);
            foreach (var schemaOverride in schemaOverrides)
                output[schemaOverride.Key] = schemaOverride.Value?.ToList() ?? [];

            return output;
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
                return DbTypesEnum.Single;

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

        private static List<InferenceRowCandidate> LoadInferenceRowCandidates(string dataXmlPath, string schemaKey)
        {
            var document = XDocument.Load(dataXmlPath);
            var root = document.Root;
            if (root == null)
                return [];

            return root.Elements()
                .Where(element => element.Name.LocalName.Equals(schemaKey, StringComparison.OrdinalIgnoreCase))
                .Select(element =>
                {
                    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var child in element.Elements())
                        values[child.Name.LocalName] = child.Value;

                    values.TryGetValue("key", out var keyValue);
                    return new InferenceRowCandidate
                    {
                        Key = keyValue ?? string.Empty,
                        Values = values,
                        Score = ScoreInferenceCandidate(values)
                    };
                })
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Key))
                .OrderByDescending(candidate => candidate.Score)
                .ToList();
        }

        private static bool TryFindRowOffset(byte[] tableData, DbTableSchema schema, string rowKey, out int rowOffset)
        {
            rowOffset = 0;
            var chunk = new ByteChunk(tableData);
            DbTableHeader.ReadData(chunk);

            var keyColumn = schema.ColumnSchemas.FirstOrDefault(x => x.Name.Equals("key", StringComparison.OrdinalIgnoreCase));

            var keyBytes = System.Text.Encoding.UTF8.GetBytes(rowKey);
            var lengthBytes = BitConverter.GetBytes((short)keyBytes.Length);

            for (var index = chunk.Index; index <= tableData.Length - keyBytes.Length - 2; index++)
            {
                if (tableData[index] != lengthBytes[0] || tableData[index + 1] != lengthBytes[1])
                    continue;

                var matches = true;
                for (var keyIndex = 0; keyIndex < keyBytes.Length; keyIndex++)
                {
                    if (tableData[index + 2 + keyIndex] == keyBytes[keyIndex])
                        continue;

                    matches = false;
                    break;
                }

                if (!matches)
                    continue;

                if (keyColumn != null)
                {
                    if (!TryReadValueForInference(tableData, index, keyColumn, keyColumn.Type, out var decodedKey, out _))
                        continue;

                    if (decodedKey is not string decodedKeyString || !decodedKeyString.Equals(rowKey, StringComparison.Ordinal))
                        continue;
                }

                rowOffset = index;
                return true;
            }

            return false;
        }

        private static bool TryInferSchemaForCandidate(
            DbTableSchema schema,
            byte[] tableData,
            string rowKey,
            IReadOnlyDictionary<string, string> expectedRow,
            int rowOffset,
            IReadOnlyList<(IReadOnlyDictionary<string, string> ExpectedRow, int RowOffset)> extraAnchors,
            out string? failureReason)
        {
            failureReason = null;
            var columns = schema.ColumnSchemas.Select(x => x.DeepClone()).ToList();

            var keyIndex = columns.FindIndex(x => x.Name.Equals("key", StringComparison.OrdinalIgnoreCase));
            if (keyIndex > 0)
            {
                var keyColumn = columns[keyIndex];
                columns.RemoveAt(keyIndex);
                columns.Insert(0, keyColumn);
            }

            var expectedRowWithKey = new Dictionary<string, string>(expectedRow, StringComparer.OrdinalIgnoreCase)
            {
                ["key"] = rowKey
            };

            // Guidance row filters out zero-valued numerics: they coincidentally match regions of
            // 4 zero bytes in the binary (e.g. two consecutive empty strings), causing the solver
            // to lock editor-only integer columns like colour_blue=0 at the wrong offset.
            // Anchor validation still uses the full expected row.
            var guidanceRow = BuildInferenceSolverGuidanceRow(expectedRowWithKey);

            var anchors = new List<(IReadOnlyDictionary<string, string> ExpectedRow, int RowOffset)>
            {
                (expectedRowWithKey, rowOffset)
            };
            anchors.AddRange(extraAnchors);

            var configs = new List<DeterministicInferenceConfig>
            {
                new() { Name = "balanced", EditBudget = 40, LookAhead = 24, MoveBaseScore = 60, MoveDistancePenalty = 1, SkipScore = -20 },
                new() { Name = "local", EditBudget = 28, LookAhead = 12, MoveBaseScore = 56, MoveDistancePenalty = 2, SkipScore = -24 },
                new() { Name = "wide", EditBudget = 56, LookAhead = 36, MoveBaseScore = 64, MoveDistancePenalty = 1, SkipScore = -16 }
            };

            var localFailures = new List<string>();
            foreach (var config in configs)
            {
                if (!TrySolveInferenceDeterministic(columns, rowOffset, tableData, guidanceRow, anchors, config, out var solvedColumns, out var localFailure))
                {
                    localFailures.Add($"{config.Name}:{localFailure}");
                    continue;
                }

                schema.ColumnSchemas = solvedColumns;
                if (VerifyInferenceAgainstAdditionalRows(schema, tableData, expectedRow, rowOffset))
                    return true;

                var typeChanges = new List<string>();
                for (var i = 0; i < Math.Min(columns.Count, solvedColumns.Count); i++)
                {
                    if (columns[i].Name.Equals(solvedColumns[i].Name, StringComparison.OrdinalIgnoreCase)
                        && columns[i].Type != solvedColumns[i].Type)
                    {
                        typeChanges.Add($"{solvedColumns[i].Name}:{columns[i].Type}->{solvedColumns[i].Type}");
                    }
                }

                var orderProbe = string.Join(",", solvedColumns.Select(x => x.Name).Take(24));
                localFailures.Add($"{config.Name}:Refined schema could not decode the table consistently after local alignment. TypeChanges=[{string.Join(";", typeChanges.Take(8))}] OrderProbe=[{orderProbe}]");
            }

            failureReason = string.Join(" || ", localFailures.Take(3));
            return false;
        }

        private static bool TrySolveInferenceDeterministic(
            List<DbColumnSchema> columns,
            int rowOffset,
            byte[] tableData,
            IReadOnlyDictionary<string, string> expectedRow,
            IReadOnlyList<(IReadOnlyDictionary<string, string> ExpectedRow, int RowOffset)> anchors,
            DeterministicInferenceConfig config,
            out List<DbColumnSchema> solvedColumns,
            out string? failureReason)
        {
            solvedColumns = [];
            failureReason = null;

            var workingColumns = columns.Select(x => x.DeepClone()).ToList();
            var editBudget = config.EditBudget;
            var index = 0;
            var offset = rowOffset;
            var lockedPrefixCount = 0;
            var iterations = 0;
            var maxIterations = Math.Max(256, workingColumns.Count * 16);

            while (index < workingColumns.Count)
            {
                iterations++;
                if (iterations > maxIterations)
                {
                    failureReason = "Deterministic inference exceeded iteration budget.";
                    return false;
                }

                var actions = GetInferenceActionsDeterministic(workingColumns, index, offset, tableData, expectedRow, editBudget, lockedPrefixCount, config);
                if (actions.Count == 0)
                {
                    failureReason = $"No deterministic action for column '{workingColumns[index].Name}' at index {index} and offset {offset}.";
                    return false;
                }

                var action = actions[0];
                if (action.Kind == InferenceActionKind.Skip)
                {
                    if (editBudget <= 0)
                    {
                        failureReason = $"Skip budget exhausted at index {index}.";
                        return false;
                    }

                    if (index < lockedPrefixCount)
                    {
                        failureReason = $"Attempted to skip locked column '{workingColumns[index].Name}'.";
                        return false;
                    }

                    workingColumns.RemoveAt(index);
                    editBudget--;
                    continue;
                }

                if (action.Kind == InferenceActionKind.Move)
                {
                    if (action.SourceIndex < lockedPrefixCount)
                    {
                        failureReason = $"Attempted to move locked column at index {action.SourceIndex}.";
                        return false;
                    }

                    var movedColumn = workingColumns[action.SourceIndex];
                    workingColumns.RemoveAt(action.SourceIndex);
                    workingColumns.Insert(index, movedColumn);
                }

                if (action.TypeOverride.HasValue)
                    workingColumns[index].Type = action.TypeOverride.Value;

                if (action.BytesRead <= 0)
                {
                    failureReason = $"Deterministic action produced invalid read width at index {index}.";
                    return false;
                }

                offset += action.BytesRead;
                index++;
                lockedPrefixCount = index;
            }

            if (!MatchesInferenceAnchors(workingColumns, tableData, anchors, out var anchorDiag))
            {
                var colDump = string.Join(",", workingColumns.Select(x => x.Name).Take(60));
                failureReason = $"Deterministic alignment failed anchor validation. AnchorDiag=[{anchorDiag}] Cols=[{colDump}]";
                return false;
            }

            solvedColumns = workingColumns;
            return true;
        }

        private static List<InferenceAction> GetInferenceActionsDeterministic(
            List<DbColumnSchema> columns,
            int index,
            int offset,
            byte[] tableData,
            IReadOnlyDictionary<string, string> expectedRow,
            int editBudget,
            int lockedPrefixCount,
            DeterministicInferenceConfig config)
        {
            var candidates = new List<InferenceAction>();

            foreach (var match in GetMatchingInferenceReads(columns[index], tableData, offset, expectedRow))
            {
                candidates.Add(new InferenceAction
                {
                    Kind = InferenceActionKind.Match,
                    Score = match.Score + 100,
                    Specificity = match.Specificity,
                    SourceIndex = index,
                    MoveDistance = 0,
                    TypeOverride = match.Type,
                    BytesRead = match.BytesRead
                });
            }

            if (expectedRow.TryGetValue(columns[index].Name, out var expectedCurrentValue)
                && string.IsNullOrWhiteSpace(expectedCurrentValue)
                && TryReadValueForInference(tableData, offset, columns[index], columns[index].Type, out _, out var blankExpectedBytesRead))
            {
                candidates.Add(new InferenceAction
                {
                    Kind = InferenceActionKind.Match,
                    Score = 1,
                    Specificity = 0,
                    SourceIndex = index,
                    MoveDistance = 0,
                    TypeOverride = columns[index].Type,
                    BytesRead = blankExpectedBytesRead
                });
            }

            if (!expectedRow.ContainsKey(columns[index].Name)
                && TryReadValueForInference(tableData, offset, columns[index], columns[index].Type, out _, out var passthroughBytesRead))
            {
                candidates.Add(new InferenceAction
                {
                    Kind = InferenceActionKind.Match,
                    Score = 5,
                    Specificity = 0,
                    SourceIndex = index,
                    MoveDistance = 0,
                    TypeOverride = columns[index].Type,
                    BytesRead = passthroughBytesRead
                });
            }

            if (editBudget > 0)
            {
                var lookAheadLimit = Math.Min(columns.Count - 1, index + config.LookAhead);
                for (var sourceIndex = Math.Max(index + 1, lockedPrefixCount); sourceIndex <= lookAheadLimit; sourceIndex++)
                {
                    var distance = sourceIndex - index;
                    foreach (var match in GetMatchingInferenceReads(columns[sourceIndex], tableData, offset, expectedRow))
                    {
                        candidates.Add(new InferenceAction
                        {
                            Kind = InferenceActionKind.Move,
                            Score = match.Score + config.MoveBaseScore - (distance * config.MoveDistancePenalty),
                            Specificity = match.Specificity,
                            SourceIndex = sourceIndex,
                            MoveDistance = distance,
                            TypeOverride = match.Type,
                            BytesRead = match.BytesRead
                        });
                    }
                }

                if (index >= lockedPrefixCount && !expectedRow.ContainsKey(columns[index].Name))
                {
                    candidates.Add(new InferenceAction
                    {
                        Kind = InferenceActionKind.Skip,
                        Score = config.SkipScore,
                        Specificity = 0,
                        SourceIndex = index,
                        MoveDistance = 0,
                        BytesRead = 0
                    });
                }

                if (expectedRow.TryGetValue(columns[index].Name, out var expectedPathValue)
                    && !string.IsNullOrWhiteSpace(expectedPathValue)
                    && IsPathLikeColumn(columns[index].Name)
                    && candidates.Count == 0)
                {
                    foreach (var candidateType in GetCandidateTypes(columns[index]))
                    {
                        if (candidateType != DbTypesEnum.String
                            && candidateType != DbTypesEnum.String_ascii
                            && candidateType != DbTypesEnum.Optstring
                            && candidateType != DbTypesEnum.Optstring_ascii)
                        {
                            continue;
                        }

                        if (!TryReadValueForInference(tableData, offset, columns[index], candidateType, out var pathDecodedValue, out var bytesRead))
                            continue;

                        if (!ValueMatchesInference(columns[index], expectedRow, pathDecodedValue, candidateType))
                            continue;

                        candidates.Add(new InferenceAction
                        {
                            Kind = InferenceActionKind.Match,
                            Score = 2,
                            Specificity = 1,
                            SourceIndex = index,
                            MoveDistance = 0,
                            TypeOverride = candidateType,
                            BytesRead = bytesRead
                        });
                    }
                }
            }

            if (candidates.Count == 0 && editBudget > 0 && index >= lockedPrefixCount)
            {
                candidates.Add(new InferenceAction
                {
                    Kind = InferenceActionKind.Skip,
                    Score = config.SkipScore - 100,
                    Specificity = 0,
                    SourceIndex = index,
                    MoveDistance = 0,
                    BytesRead = 0
                });
            }

            return candidates
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Specificity)
                .ThenBy(x => x.MoveDistance)
                .ThenBy(x => x.Kind == InferenceActionKind.Match ? 0 : x.Kind == InferenceActionKind.Move ? 1 : 2)
                .ThenBy(x => x.SourceIndex)
                .ToList();
        }

        private static bool TrySolveInference(
            List<DbColumnSchema> columns,
            int index,
            int offset,
            int editBudget,
            byte[] tableData,
            IReadOnlyDictionary<string, string> expectedRow,
            IReadOnlyList<(IReadOnlyDictionary<string, string> ExpectedRow, int RowOffset)> anchors,
            HashSet<string> deadStates,
            out List<DbColumnSchema> solvedColumns,
            out string? failureReason)
        {
            solvedColumns = [];
            failureReason = null;

            if (deadStates.Count > MaxInferenceDeadStates)
            {
                failureReason = "Inference search exceeded state budget.";
                return false;
            }

            if (index >= columns.Count)
            {
                if (!MatchesInferenceAnchors(columns, tableData, anchors))
                {
                    failureReason = "Schema alignment failed extra anchor validation.";
                    return false;
                }

                solvedColumns = columns;
                return true;
            }

            var stateKey = $"{index}|{offset}|{editBudget}|{string.Join(',', columns.Skip(index).Take(18).Select(x => $"{x.Name}:{(int)x.Type}"))}";
            if (deadStates.Contains(stateKey))
            {
                failureReason = $"Previously failed state at index {index} and offset {offset}.";
                return false;
            }

            var actions = GetInferenceActions(columns, index, offset, tableData, expectedRow, editBudget);
            if (actions.Count == 0)
            {
                deadStates.Add(stateKey);
                failureReason = $"No viable action for column '{columns[index].Name}' at offset {offset}.";
                return false;
            }

            foreach (var action in actions)
            {
                var nextColumns = columns.Select(x => x.DeepClone()).ToList();
                var nextIndex = index;
                var nextOffset = offset;
                var nextEditBudget = editBudget;

                if (action.Kind == InferenceActionKind.Skip)
                {
                    nextColumns.RemoveAt(index);
                    nextEditBudget--;
                }
                else
                {
                    if (action.Kind == InferenceActionKind.Move)
                    {
                        var movedColumn = nextColumns[action.SourceIndex];
                        nextColumns.RemoveAt(action.SourceIndex);
                        nextColumns.Insert(index, movedColumn);
                    }

                    if (action.TypeOverride.HasValue)
                        nextColumns[index].Type = action.TypeOverride.Value;

                    nextOffset += action.BytesRead;
                    nextIndex++;
                }

                if (TrySolveInference(nextColumns, nextIndex, nextOffset, nextEditBudget, tableData, expectedRow, anchors, deadStates, out solvedColumns, out failureReason))
                    return true;
            }

            deadStates.Add(stateKey);
            if (index == 0)
            {
                var actionProbe = string.Join(",", actions.Take(6).Select(x => $"{x.Kind}:{x.SourceIndex}:{x.Score}:{x.BytesRead}"));
                var hasExpectedValue = expectedRow.ContainsKey(columns[index].Name);
                failureReason = $"Backtracking exhausted at index {index} and offset {offset}. FirstColumn={columns[index].Name}:{columns[index].Type},HasExpected={hasExpectedValue},ActionProbe=[{actionProbe}]";
                return false;
            }

            failureReason = $"Backtracking exhausted at index {index} and offset {offset}.";
            return false;
        }

        private static bool MatchesInferenceAnchors(
            IReadOnlyList<DbColumnSchema> columns,
            byte[] tableData,
            IReadOnlyList<(IReadOnlyDictionary<string, string> ExpectedRow, int RowOffset)> anchors,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            foreach (var anchor in anchors)
            {
                if (!TryMatchSchemaAtOffset(columns, tableData, anchor.ExpectedRow, anchor.RowOffset, out var matchDiag))
                {
                    var anchorKey = anchor.ExpectedRow.TryGetValue("key", out var k) ? k : "?";
                    diagnostic = $"anchor={anchorKey}:{matchDiag}";
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesInferenceAnchors(
            IReadOnlyList<DbColumnSchema> columns,
            byte[] tableData,
            IReadOnlyList<(IReadOnlyDictionary<string, string> ExpectedRow, int RowOffset)> anchors)
            => MatchesInferenceAnchors(columns, tableData, anchors, out _);

        private static bool TryMatchSchemaAtOffset(
            IReadOnlyList<DbColumnSchema> columns,
            byte[] tableData,
            IReadOnlyDictionary<string, string> expectedRow,
            int rowOffset)
            => TryMatchSchemaAtOffset(columns, tableData, expectedRow, rowOffset, out _);

        private static bool TryMatchSchemaAtOffset(
            IReadOnlyList<DbColumnSchema> columns,
            byte[] tableData,
            IReadOnlyDictionary<string, string> expectedRow,
            int rowOffset,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            var offset = rowOffset;
            foreach (var column in columns)
            {
                if (!TryReadValueForInference(tableData, offset, column, column.Type, out var decodedValue, out var bytesRead))
                {
                    diagnostic = $"ReadFail@{column.Name}(offset={offset})";
                    return false;
                }

                if (expectedRow.ContainsKey(column.Name) && !ValueMatchesInference(column, expectedRow, decodedValue, column.Type))
                {
                    var expVal = expectedRow[column.Name];
                    diagnostic = $"ValMismatch@{column.Name}(offset={offset},exp={expVal},got={decodedValue})";
                    return false;
                }

                offset += bytesRead;
            }

            return true;
        }

        private static int ScoreInferenceCandidate(IReadOnlyDictionary<string, string> values)
        {
            var score = 0;

            foreach (var entry in values)
            {
                if (entry.Key.Equals("key", StringComparison.OrdinalIgnoreCase))
                {
                    score += 12;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.Value))
                    continue;

                if (TryParseBoolean(entry.Value, out var boolValue))
                {
                    score += boolValue ? 4 : 2;
                    continue;
                }

                if (double.TryParse(entry.Value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var numericValue))
                {
                    score += Math.Abs(numericValue) > 1d ? 6 : 3;
                    continue;
                }

                score += 5;
            }

            return score;
        }

        private sealed class InferenceAction
        {
            public required InferenceActionKind Kind { get; init; }
            public required int Score { get; init; }
            public required int Specificity { get; init; }
            public int SourceIndex { get; init; }
            public int MoveDistance { get; init; }
            public DbTypesEnum? TypeOverride { get; init; }
            public int BytesRead { get; init; }
        }

        private sealed class DeterministicInferenceConfig
        {
            public required string Name { get; init; }
            public required int EditBudget { get; init; }
            public required int LookAhead { get; init; }
            public required int MoveBaseScore { get; init; }
            public required int MoveDistancePenalty { get; init; }
            public required int SkipScore { get; init; }
        }

        private enum InferenceActionKind
        {
            Match,
            Skip,
            Move
        }

        private static List<InferenceAction> GetInferenceActions(
            List<DbColumnSchema> columns,
            int index,
            int offset,
            byte[] tableData,
            IReadOnlyDictionary<string, string> expectedRow,
            int editBudget)
        {
            if (index >= columns.Count)
                return [];

            var candidates = new List<InferenceAction>();

            foreach (var match in GetMatchingInferenceReads(columns[index], tableData, offset, expectedRow))
            {
                candidates.Add(new InferenceAction
                {
                    Kind = InferenceActionKind.Match,
                    Score = match.Score,
                    Specificity = match.Specificity,
                    SourceIndex = index,
                    TypeOverride = match.Type,
                    BytesRead = match.BytesRead
                });
            }

            if (expectedRow.TryGetValue(columns[index].Name, out var expectedCurrentValue)
                && string.IsNullOrWhiteSpace(expectedCurrentValue)
                && TryReadValueForInference(tableData, offset, columns[index], columns[index].Type, out _, out var blankExpectedBytesRead))
            {
                candidates.Add(new InferenceAction
                {
                    Kind = InferenceActionKind.Match,
                    Score = -2,
                    Specificity = 0,
                    SourceIndex = index,
                    TypeOverride = columns[index].Type,
                    BytesRead = blankExpectedBytesRead
                });
            }

            if (!expectedRow.ContainsKey(columns[index].Name)
                && TryReadValueForInference(tableData, offset, columns[index], columns[index].Type, out _, out var passthroughBytesRead))
            {
                candidates.Add(new InferenceAction
                {
                    Kind = InferenceActionKind.Match,
                    Score = -1,
                    Specificity = 0,
                    SourceIndex = index,
                    TypeOverride = columns[index].Type,
                    BytesRead = passthroughBytesRead
                });
            }

            if (editBudget > 0)
            {
                if (!expectedRow.ContainsKey(columns[index].Name))
                {
                    candidates.Add(new InferenceAction
                    {
                        Kind = InferenceActionKind.Skip,
                        Score = 1,
                        Specificity = 0,
                        SourceIndex = index,
                        BytesRead = 0
                    });
                }

                var lookAheadLimit = Math.Min(columns.Count - 1, index + 12);
                for (var sourceIndex = index + 1; sourceIndex <= lookAheadLimit; sourceIndex++)
                {
                    var distance = sourceIndex - index;
                    foreach (var match in GetMatchingInferenceReads(columns[sourceIndex], tableData, offset, expectedRow))
                    {
                        candidates.Add(new InferenceAction
                        {
                            Kind = InferenceActionKind.Move,
                            Score = match.Score - 6 - distance,
                            Specificity = match.Specificity,
                            SourceIndex = sourceIndex,
                            TypeOverride = match.Type,
                            BytesRead = match.BytesRead
                        });
                    }
                }
            }

            if (candidates.Count == 0 && editBudget > 0)
            {
                candidates.Add(new InferenceAction
                {
                    Kind = InferenceActionKind.Skip,
                    Score = -100,
                    Specificity = 0,
                    SourceIndex = index,
                    BytesRead = 0
                });
            }

            return candidates
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Specificity)
                .ThenBy(x => x.Kind == InferenceActionKind.Match ? 0 : x.Kind == InferenceActionKind.Move ? 1 : 2)
                .ThenBy(x => x.SourceIndex)
                .ToList();
        }

        private sealed class InferenceReadMatch
        {
            public required DbTypesEnum Type { get; init; }
            public required object? Value { get; init; }
            public required int BytesRead { get; init; }
            public required int Score { get; init; }
            public required int Specificity { get; init; }
        }

        private static IEnumerable<InferenceReadMatch> GetMatchingInferenceReads(DbColumnSchema column, byte[] tableData, int offset, IReadOnlyDictionary<string, string> expectedRow)
        {
            if (!expectedRow.TryGetValue(column.Name, out var expectedValue))
                yield break;

            foreach (var candidateType in GetCandidateTypes(column))
            {
                if (!TryReadValueForInference(tableData, offset, column, candidateType, out var decodedValue, out var bytesRead))
                    continue;

                if (!ValueMatchesInference(column, expectedRow, decodedValue, candidateType))
                    continue;

                var specificity = GetInferenceSpecificity(candidateType, expectedValue, decodedValue);
                yield return new InferenceReadMatch
                {
                    Type = candidateType,
                    Value = decodedValue,
                    BytesRead = bytesRead,
                    Score = specificity,
                    Specificity = specificity
                };
            }
        }

        private static IEnumerable<DbTypesEnum> GetCandidateTypes(DbColumnSchema column)
        {
            var yielded = new HashSet<DbTypesEnum>();

            bool YieldUnique(DbTypesEnum type)
            {
                if (yielded.Contains(type))
                    return false;

                yielded.Add(type);
                return true;
            }

            if (YieldUnique(column.Type))
                yield return column.Type;

            if (column.Type == DbTypesEnum.Single || column.Type == DbTypesEnum.Double)
            {
                if (YieldUnique(DbTypesEnum.Single))
                    yield return DbTypesEnum.Single;

                if (YieldUnique(DbTypesEnum.Double))
                    yield return DbTypesEnum.Double;
            }

            if (column.Type == DbTypesEnum.Integer
                || column.Type == DbTypesEnum.Int64
                || column.Type == DbTypesEnum.Short
                || column.Type == DbTypesEnum.UShort
                || column.Type == DbTypesEnum.Byte
                || column.Type == DbTypesEnum.uint32)
            {
                if (YieldUnique(DbTypesEnum.Integer))
                    yield return DbTypesEnum.Integer;

                if (YieldUnique(DbTypesEnum.Int64))
                    yield return DbTypesEnum.Int64;

                if (YieldUnique(DbTypesEnum.Short))
                    yield return DbTypesEnum.Short;

                if (YieldUnique(DbTypesEnum.UShort))
                    yield return DbTypesEnum.UShort;

                if (YieldUnique(DbTypesEnum.Byte))
                    yield return DbTypesEnum.Byte;

                if (YieldUnique(DbTypesEnum.uint32))
                    yield return DbTypesEnum.uint32;
            }

            if (column.Type == DbTypesEnum.String
                || column.Type == DbTypesEnum.String_ascii
                || column.Type == DbTypesEnum.Optstring
                || column.Type == DbTypesEnum.Optstring_ascii)
            {
                if (YieldUnique(DbTypesEnum.String))
                    yield return DbTypesEnum.String;

                if (YieldUnique(DbTypesEnum.String_ascii))
                    yield return DbTypesEnum.String_ascii;

                if (YieldUnique(DbTypesEnum.Optstring))
                    yield return DbTypesEnum.Optstring;

                if (YieldUnique(DbTypesEnum.Optstring_ascii))
                    yield return DbTypesEnum.Optstring_ascii;
            }
        }

        private static bool TryReadValueForInference(byte[] tableData, int offset, DbColumnSchema column, out object? value, out int bytesRead)
        {
            return TryReadValueForInference(tableData, offset, column, column.Type, out value, out bytesRead);
        }

        private static bool TryReadValueForInference(byte[] tableData, int offset, DbColumnSchema column, DbTypesEnum type, out object? value, out int bytesRead)
        {
            value = null;
            bytesRead = 0;

            try
            {
                var data = new ByteChunk(tableData, offset);

                if (column.StringSerialisationMode == DbStringSerialisationMode.FixedLengthZeroTerminatedUtf8
                    && (type == DbTypesEnum.String || type == DbTypesEnum.String_ascii || type == DbTypesEnum.Optstring || type == DbTypesEnum.Optstring_ascii))
                {
                    if (column.MaxLength <= 0 || data.BytesLeft < column.MaxLength)
                        return false;

                    var rawValue = System.Text.Encoding.UTF8.GetString(data.Buffer, data.Index, column.MaxLength);
                    var zeroTerminatorIndex = rawValue.IndexOf('\0');
                    value = zeroTerminatorIndex >= 0 ? rawValue[..zeroTerminatorIndex] : rawValue;
                    bytesRead = column.MaxLength;
                    return true;
                }

                if (type == DbTypesEnum.List)
                    return false;

                if (type == DbTypesEnum.Optstring || type == DbTypesEnum.Optstring_ascii)
                {
                    if (data.BytesLeft < 1)
                        return false;

                    var optionalFlag = data.Buffer[data.Index];
                    if (optionalFlag <= 1)
                    {
                        var parser = ByteParsers.GetParser(type);
                        value = parser.GetValueAsObject(data.Buffer, data.Index, out bytesRead);
                        return true;
                    }

                    var fallbackType = type == DbTypesEnum.Optstring ? DbTypesEnum.String : DbTypesEnum.String_ascii;
                    var fallbackParser = ByteParsers.GetParser(fallbackType);
                    value = fallbackParser.GetValueAsObject(data.Buffer, data.Index, out bytesRead);
                    return true;
                }

                if (column.IsOptional && type != DbTypesEnum.Boolean)
                {
                    if (data.BytesLeft < 1)
                        return false;

                    var optionalFlag = data.Buffer[data.Index];
                    if (optionalFlag <= 1)
                    {
                        var hasValue = data.ReadBool();
                        var parser = ByteParsers.GetParser(type);
                        value = parser.GetValueAsObject(data.Buffer, data.Index, out var parserBytesRead);
                        data.Advance(parserBytesRead);
                        bytesRead = data.Index - offset;
                        return hasValue || value != null;
                    }
                }

                var requiredParser = ByteParsers.GetParser(type);
                value = requiredParser.GetValueAsObject(data.Buffer, data.Index, out bytesRead);
                return true;
            }
            catch
            {
                value = null;
                bytesRead = 0;
                return false;
            }
        }

        private static bool ValueMatchesInference(DbColumnSchema column, IReadOnlyDictionary<string, string> expectedRow, object? decodedValue, DbTypesEnum type)
        {
            if (!expectedRow.TryGetValue(column.Name, out var expectedValue))
                return false;

            if (type == DbTypesEnum.String || type == DbTypesEnum.String_ascii || type == DbTypesEnum.Optstring || type == DbTypesEnum.Optstring_ascii)
            {
                var decodedString = decodedValue?.ToString() ?? string.Empty;
                if (string.Equals(decodedString, expectedValue, StringComparison.Ordinal))
                    return true;

                if (IsPathLikeColumn(column.Name))
                    return string.Equals(NormalisePathForInference(decodedString), NormalisePathForInference(expectedValue), StringComparison.OrdinalIgnoreCase);

                return false;
            }

            if (type == DbTypesEnum.Boolean)
            {
                if (!TryParseBoolean(expectedValue, out var expectedBool))
                    return false;

                return decodedValue is bool decodedBool && decodedBool == expectedBool;
            }

            if (type == DbTypesEnum.Single || type == DbTypesEnum.Double)
            {
                if (!double.TryParse(expectedValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var expectedNumber))
                    return false;

                var decodedNumber = Convert.ToDouble(decodedValue, CultureInfo.InvariantCulture);
                return Math.Abs(decodedNumber - expectedNumber) < 0.0001d;
            }

            if (type == DbTypesEnum.Integer || type == DbTypesEnum.Int64 || type == DbTypesEnum.Short || type == DbTypesEnum.UShort || type == DbTypesEnum.Byte || type == DbTypesEnum.uint32)
            {
                if (!long.TryParse(expectedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedInteger))
                    return false;

                var decodedInteger = Convert.ToInt64(decodedValue, CultureInfo.InvariantCulture);
                return decodedInteger == expectedInteger;
            }

            return string.Equals(Convert.ToString(decodedValue, CultureInfo.InvariantCulture), expectedValue, StringComparison.Ordinal);
        }

        private static int GetInferenceSpecificity(DbTypesEnum type, string expectedValue, object? decodedValue)
        {
            if (type == DbTypesEnum.String || type == DbTypesEnum.String_ascii || type == DbTypesEnum.Optstring || type == DbTypesEnum.Optstring_ascii)
                return string.IsNullOrWhiteSpace(expectedValue) ? 20 : 90;

            if (type == DbTypesEnum.Boolean)
                return decodedValue is bool boolValue && boolValue ? 60 : 40;

            if (type == DbTypesEnum.Single || type == DbTypesEnum.Double)
            {
                if (double.TryParse(expectedValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var numeric))
                    return Math.Abs(numeric) > 1d ? 80 : 50;

                return 35;
            }

            if (long.TryParse(expectedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
                return Math.Abs(integerValue) > 1 ? 70 : 30;

            return 15;
        }

        private static bool IsPathLikeColumn(string columnName)
        {
            return columnName.Equals("path", StringComparison.OrdinalIgnoreCase)
                || columnName.EndsWith("_path", StringComparison.OrdinalIgnoreCase)
                || columnName.Contains("path", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalisePathForInference(string value)
        {
            return value
                .Trim()
                .Trim('"')
                .Replace('\\', '/');
        }

        private static bool VerifyInferenceAgainstAdditionalRows(DbTableSchema schema, byte[] tableData, IReadOnlyDictionary<string, string> seedRow, int seedRowOffset)
        {
            return TryMatchSchemaAtOffset(schema.ColumnSchemas, tableData, seedRow, seedRowOffset);
        }

        private static bool TryParseBoolean(string value, out bool parsed)
        {
            if (string.Equals(value, "1", StringComparison.Ordinal))
            {
                parsed = true;
                return true;
            }

            if (string.Equals(value, "0", StringComparison.Ordinal))
            {
                parsed = false;
                return true;
            }

            return bool.TryParse(value, out parsed);
        }


        private static IReadOnlyList<string> GetSchemaOverrideCandidatePaths(GameTypeEnum game)
        {
            var overrideNames = GetSchemaNameCandidates(game)
                .Select(name => $"{Path.GetFileNameWithoutExtension(name)}.overrides.json")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var output = new List<string>(overrideNames.Count);
            foreach (var overrideName in overrideNames)
            {
                output.Add(Path.Combine(DirectoryHelper.SchemaDirectory, overrideName));
                output.Add(Path.Combine(AppContext.BaseDirectory, "Data", "Schema", overrideName));
                output.Add(Path.Combine(Environment.CurrentDirectory, "Data", "Schema", overrideName));
            }

            return output;
        }
    }
}
