using Serilog;
using Shared.ByteParsing;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;

namespace Shared.GameFormats.Db
{
    public interface IDbTableQueryService
    {
        DbTable LoadTable(PackFile packFile, string directory);
        List<DbTable> LoadTables(string directory, List<IPackFileContainer> containers);
    }

    public class DbTableQueryService(IDbSchemaManager schemaManager) : IDbTableQueryService
    {
        private readonly ILogger _logger = Logging.Create<DbTableQueryService>();
        private readonly IDbSchemaManager _schemaManager = schemaManager;

        public DbTable LoadTable(PackFile packFile, string directory)
        {
            _schemaManager.EnsureLoaded();
            var data = packFile.DataSource.ReadData();
            var header = DbTableHeader.ReadData(new ByteChunk(data));
            var schema = _schemaManager.GetSchema(directory, header.Version);

            try
            {
                return DbTable.CreateFromBytes(data, packFile.Name, schema);
            }
            catch
            {
                if (!_schemaManager.TryInferSchemaFromAssemblyKitData(directory, header.Version, data, schema, out var refinedSchema))
                    throw;

                try
                {
                    return DbTable.CreateFromBytes(data, packFile.Name, refinedSchema);
                }
                catch
                {
                    if (_schemaManager.TryGetLocatedAssemblyKitRowOffsets(directory, header.Version, data, refinedSchema, out var locatedRows))
                        return DbTable.CreateFromBytesAtOffsets(data, packFile.Name, refinedSchema, locatedRows.Select(x => x.RowOffset).ToList());

                    throw;
                }
            }
        }

        public List<DbTable> LoadTables(string tablesDirectory, List<IPackFileContainer> containers)
        {
            _schemaManager.EnsureLoaded();

            var tables = new List<DbTable>();

            var tablesDirectoryPath = $"db\\{tablesDirectory}";
            var packFiles = PackFileServiceUtility.GetDirectoryFiles(tablesDirectoryPath, containers);
            if (packFiles.Count == 0)
            {
                _logger.Here().Warning($"Unable to resolve Db directory {tablesDirectory}.");
                return tables;
            }

            foreach (var tablePackFile in packFiles)
                tables.Add(LoadTable(tablePackFile, tablesDirectory));

            return tables;
        }

    }
}
