using Shared.ByteParsing;
using Shared.Core.PackFiles.Models;

namespace Shared.GameFormats.DB
{
    public interface IDbTableQueryService
    {
        DbTable LoadTable(PackFile packFile, string directory);
        List<DbTable> LoadTables(string directory, List<IPackFileContainer> containers);
        IReadOnlyDictionary<string, List<DbTable>> LoadTables(List<string> directories, List<IPackFileContainer> containers);
    }

    public class DbTableQueryService(IDbSchemaManager schemaManager) : IDbTableQueryService
    {
        private readonly IDbSchemaManager _schemaManager = schemaManager;

        public DbTable LoadTable(PackFile packFile, string directory)
        {
            var data = packFile.DataSource.ReadData();
            var header = DbTableHeader.ReadData(new ByteChunk(data));
            var schema = _schemaManager.GetSchema(directory, header.Version);
            return DbTable.CreateFromBytes(data, packFile.Name, schema);
        }

        public List<DbTable> LoadTables(string tablesDirectory, List<IPackFileContainer> containers)
        {
            if (string.IsNullOrWhiteSpace(tablesDirectory))
                return [];
            var normalisedDirectory = NormaliseDirectory(tablesDirectory);
            return LoadTables([normalisedDirectory], containers)[normalisedDirectory];
        }

        public IReadOnlyDictionary<string, List<DbTable>> LoadTables(List<string> directories, List<IPackFileContainer> containers)
        {
            ArgumentNullException.ThrowIfNull(directories);
            ArgumentNullException.ThrowIfNull(containers);

            var tablesByDirectory = directories
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Select(NormaliseDirectory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(directory => directory, _ => new List<DbTable>(), StringComparer.OrdinalIgnoreCase);

            if (tablesByDirectory.Count == 0)
                return tablesByDirectory;

            foreach (var container in containers)
            {
                foreach (var (tablePath, tableFile) in container.GetAllFiles())
                {
                    var directory = GetTableDirectory(tablePath);
                    if (directory == null || !tablesByDirectory.TryGetValue(directory, out var tables))
                        continue;

                    try
                    {
                        tables.Add(LoadTable(tableFile, directory));
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidDataException($"Failed to load DB table file '{tablePath}'.", exception);
                    }
                }
            }

            return tablesByDirectory;
        }

        private static string NormaliseDirectory(string directory) => directory.Trim('/', '\\');

        private static string? GetTableDirectory(string path)
        {
            var normalisedPath = path.Replace('\\', '/');
            var databasePrefix = "db/";
            if (!normalisedPath.StartsWith(databasePrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var directoryEnd = normalisedPath.IndexOf('/', databasePrefix.Length);
            if (directoryEnd <= databasePrefix.Length)
                return null;
            else
                return (string?)normalisedPath[databasePrefix.Length..directoryEnd];
        }
    }
}
