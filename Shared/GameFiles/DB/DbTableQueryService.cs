using Shared.ByteParsing;
using Shared.Core.PackFiles.Models;

namespace Shared.GameFormats.DB
{
    public interface IDbTableQueryService
    {
        DbTable LoadTable(PackFile packFile, string directory);
        List<DbTable> LoadTables(string directory, List<IPackFileContainer> containers);
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
            var tables = new List<DbTable>();
            var directoryPrefix = $"db/{tablesDirectory.Trim('/', '\\')}/";

            foreach (var container in containers)
            {
                var tableFiles = container.GetAllFiles()
                    .Where(x => x.Key.Replace('\\', '/').StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var (tablePath, tableFile) in tableFiles)
                {
                    try
                    {
                        tables.Add(LoadTable(tableFile, tablesDirectory));
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidDataException($"Failed to load DB table file '{tablePath}'.", exception);
                    }
                }
            }

            return tables;
        }
    }
}
