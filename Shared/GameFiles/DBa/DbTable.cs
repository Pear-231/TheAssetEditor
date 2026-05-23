using Shared.ByteParsing;
using Shared.ByteParsing.Parsers;
using Shared.Core.PackFiles.Models;

namespace Shared.GameFormats.DB
{
    public class DbTable
    {
        public string TableName { get; set; } = string.Empty;
        public DbTableSchema Schema { get; set; } = new DbTableSchema();
        public DbTableHeader Header { get; set; } = new DbTableHeader();
        public List<DbTableRow> Rows { get; set; } = [];

        public static DbTable CreateFromPackFile(PackFile file, string tableName, DbTableSchema schema)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            return CreateFromBytes(file.DataSource.ReadData(), tableName, schema);
        }

        public static DbTable CreateFromBytes(byte[] fileContent, string tableName, DbTableSchema schema)
        {
            if (fileContent == null)
                throw new ArgumentNullException(nameof(fileContent));
            if (schema == null)
                throw new ArgumentNullException(nameof(schema));

            var table = new DbTable
            {
                TableName = tableName,
                Schema = schema
            };

            table.ReadData(fileContent);
            return table;
        }

        public void ReadData(byte[] fileContent)
        {
            if (fileContent == null)
                throw new ArgumentNullException(nameof(fileContent));

            if (Schema == null)
                throw new ArgumentNullException(nameof(Schema));

            var data = new ByteChunk(fileContent);
            var header = DbTableHeader.ReadData(data);

            var rows = new List<DbTableRow>((int)header.EntryCount);
            for (var rowIndex = 0; rowIndex < header.EntryCount; rowIndex++)
            {
                var row = new DbTableRow();

                foreach (var column in Schema.ColumnSchemas)
                {
                    try
                    {
                        row.Values[column.Name] = ReadColumnValue(data, column);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException($"Failed parsing table '{TableName}' row {rowIndex} column '{column.Name}'", ex);
                    }
                }

                rows.Add(row);
            }

            Header = header;
            Rows = rows;
        }

        private static object? ReadColumnValue(ByteChunk data, DbColumnSchema column)
        {
            if (column.Type == DbTypesEnum.StringLookup)
            {
                var hasValue = data.ReadBool();
                var value = data.ReadInt32();
                if (!hasValue)
                    return null;

                return value;
            }

            if (column.Type == DbTypesEnum.List)
                throw new NotSupportedException($"Unsupported Db field type: {column.Type}");

            if (column.IsOptional && column.Type != DbTypesEnum.Optstring && column.Type != DbTypesEnum.Optstring_ascii)
            {
                var hasValue = data.ReadBool();
                var parser = ByteParsers.GetParser(column.Type);
                var value = parser.GetValueAsObject(data.Buffer, data.Index, out var bytesRead);
                data.Advance(bytesRead);
                if (!hasValue)
                    return null;

                return value;
            }

            var parserForRequired = ByteParsers.GetParser(column.Type);
            var requiredValue = parserForRequired.GetValueAsObject(data.Buffer, data.Index, out var requiredBytesRead);
            data.Advance(requiredBytesRead);
            return requiredValue;
        }
    }
}
