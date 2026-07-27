using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Octokit;
using Shared.Core.Misc;
using Shared.GameFormats.DB.Rpfm;

namespace Shared.GameFormats.DB
{
    public interface IDbSchemaManager
    {
        Task InitializeAsync(CancellationToken cancellationToken = default);
        DbTableSchema GetSchema(string tableName, int tableVersion);
    }

    public sealed class DbSchemaManager : IDbSchemaManager
    {
        private const string GitHubOwner = "Frodo45127";
        private const string GitHubRepository = "rpfm-schemas";
        private const string GitHubBranch = "master";
        private const string Wh3SchemaPath = "schema_wh3.ron";
        private const string CacheFileName = "schema_wh3.json";

        private static readonly ILogger s_logger = Logging.Create<DbSchemaManager>();
        private static readonly GitHubClient s_gitHubClient = new(new ProductHeaderValue("AssetEditor"));

        private readonly object _schemaLock = new();
        private readonly string _cacheDirectory;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly TaskCompletionSource _schemaReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _initializationTask;
        private string _cachedSha = string.Empty;
        private Dictionary<string, List<DbTableSchema>> _tableSchemas = new(StringComparer.OrdinalIgnoreCase);

        public DbSchemaManager()
        {
            _cacheDirectory = Path.Combine(DirectoryHelper.Temp, "Db");
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            lock (_schemaLock)
                return _initializationTask ??= InitializeCoreAsync(cancellationToken);
        }

        public DbTableSchema GetSchema(string tableName, int tableVersion)
        {
            if (TryGetSchema(tableName, tableVersion, out var schema))
                return schema;

            var initialization = InitializeAsync();
            _schemaReady.Task.GetAwaiter().GetResult();
            if (TryGetSchema(tableName, tableVersion, out schema))
                return schema;

            initialization.GetAwaiter().GetResult();
            if (TryGetSchema(tableName, tableVersion, out schema))
                return schema;

            lock (_schemaLock)
            {
                if (!_tableSchemas.ContainsKey(tableName))
                    throw new NotSupportedException($"No RPFM schema is available for DB table '{tableName}'.");
            }

            throw new NotSupportedException($"No RPFM schema is available for DB table '{tableName}' version {tableVersion}.");
        }

        public static DbSchemaManager CreateFromRon(string schemaText)
        {
            var manager = new DbSchemaManager();
            manager.ReplaceSchemas(new RonReader(schemaText).ReadTableSchemas());
            manager._initializationTask = Task.CompletedTask;
            manager._schemaReady.TrySetResult();
            return manager;
        }

        private async Task InitializeCoreAsync(CancellationToken cancellationToken)
        {
            var hasCachedSchema = await TryLoadCacheAsync(cancellationToken);
            if (hasCachedSchema)
                _schemaReady.TrySetResult();

            try
            {
                await UpdateFromGitHubAsync(cancellationToken);
                _schemaReady.TrySetResult();
            }
            catch (Exception exception)
            {
                s_logger.Information($"Unable to update the RPFM WH3 schema: {exception.Message}");
                if (!hasCachedSchema)
                {
                    var unavailableException = new InvalidOperationException("The WH3 DB schema could not be downloaded and no cached copy is available.", exception);
                    _schemaReady.TrySetException(unavailableException);
                    throw unavailableException;
                }
            }
        }

        private async Task<bool> TryLoadCacheAsync(CancellationToken cancellationToken)
        {
            var cachePath = Path.Combine(_cacheDirectory, CacheFileName);
            if (!File.Exists(cachePath))
                return false;

            try
            {
                await using var stream = File.OpenRead(cachePath);
                var cache = await JsonSerializer.DeserializeAsync<DbSchemaCache>(
                    stream,
                    _jsonOptions,
                    cancellationToken);
                if (cache == null || cache.TableSchemas.Count == 0)
                    return false;

                ReplaceSchemas(cache.TableSchemas, cache.Sha);
                return true;
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                s_logger.Information($"Unable to load the cached RPFM WH3 schema: {exception.Message}");
                return false;
            }
        }

        private async Task UpdateFromGitHubAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schemaContents = await s_gitHubClient.Repository.Content.GetAllContentsByRef(
                GitHubOwner,
                GitHubRepository,
                Wh3SchemaPath,
                GitHubBranch);
            if (schemaContents.Count != 1)
                throw new InvalidDataException($"Expected one '{Wh3SchemaPath}' entry from GitHub, found {schemaContents.Count}.");

            var latestSchema = schemaContents[0];

            if (HasSchemas()
                && string.Equals(GetCachedSha(), latestSchema.Sha, StringComparison.OrdinalIgnoreCase))
                return;

            cancellationToken.ThrowIfCancellationRequested();
            var schemaBytes = await s_gitHubClient.Repository.Content.GetRawContentByRef(
                GitHubOwner,
                GitHubRepository,
                Wh3SchemaPath,
                GitHubBranch);
            var ron = Encoding.UTF8.GetString(schemaBytes);
            var schemas = await Task.Run(
                () => new RonReader(ron).ReadTableSchemas(),
                cancellationToken);

            Directory.CreateDirectory(_cacheDirectory);
            var cachePath = Path.Combine(_cacheDirectory, CacheFileName);
            var temporaryPath = cachePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                var cache = new DbSchemaCache
                {
                    Sha = latestSchema.Sha,
                    TableSchemas = schemas
                };
                await JsonSerializer.SerializeAsync(stream, cache, _jsonOptions, cancellationToken);
            }
            File.Move(temporaryPath, cachePath, true);

            ReplaceSchemas(schemas, latestSchema.Sha);
        }

        private bool TryGetSchema(string tableName, int tableVersion, out DbTableSchema schema)
        {
            lock (_schemaLock)
            {
                if (_tableSchemas.TryGetValue(tableName, out var definitions))
                {
                    var match = definitions.FirstOrDefault(x => x.Version == tableVersion);
                    if (match != null)
                    {
                        schema = match;
                        return true;
                    }
                }
            }

            schema = null!;
            return false;
        }

        private bool HasSchemas()
        {
            lock (_schemaLock)
                return _tableSchemas.Count > 0;
        }

        private string GetCachedSha()
        {
            lock (_schemaLock)
                return _cachedSha;
        }

        private void ReplaceSchemas(
            Dictionary<string, List<DbTableSchema>> schemas,
            string sha = "")
        {
            lock (_schemaLock)
            {
                _tableSchemas = new Dictionary<string, List<DbTableSchema>>(
                    schemas,
                    StringComparer.OrdinalIgnoreCase);
                _cachedSha = sha;
            }
        }

        private sealed class DbSchemaCache
        {
            public string Sha { get; set; } = string.Empty;
            public Dictionary<string, List<DbTableSchema>> TableSchemas { get; set; } = [];
        }

    }
}
