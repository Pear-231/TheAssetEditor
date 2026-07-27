using Shared.ByteParsing;
using Shared.ByteParsing.Parsers;
using Shared.Core.PackFiles.Models;

namespace Shared.GameFormats.DB
{
    public class DbTable
    {
        public string TableName { get; set; } = string.Empty;
        public DbTableSchema Schema { get; set; } = new();
        public DbTableHeader Header { get; set; } = new();
        public List<DbTableRow> Rows { get; set; } = [];

        public static DbTable CreateFromPackFile(PackFile file, string tableName, DbTableSchema schema)
        {
            ArgumentNullException.ThrowIfNull(file);
            return CreateFromBytes(file.DataSource.ReadData(), tableName, schema);
        }

        public static DbTable CreateFromBytes(byte[] fileContent, string tableName, DbTableSchema schema)
        {
            ArgumentNullException.ThrowIfNull(fileContent);
            ArgumentNullException.ThrowIfNull(schema);

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
            ArgumentNullException.ThrowIfNull(fileContent);
            var data = new ByteChunk(fileContent);
            var header = DbTableHeader.ReadData(data);
            var rows = new List<DbTableRow>((int)header.EntryCount);

            for (var rowIndex = 0; rowIndex < header.EntryCount; rowIndex++)
            {
                var row = new DbTableRow();
                try
                {
                    foreach (var column in Schema.ColumnSchemas)
                        row.Values[column.Name] = ReadColumnValue(data, column);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Failed to decode row {rowIndex} in table '{TableName}'. {ex.Message}", ex);
                }

                rows.Add(row);
            }

            if (data.BytesLeft != 0)
                throw new InvalidDataException($"Decoded table '{TableName}' has {data.BytesLeft} trailing bytes.");

            Header = header;
            Rows = rows;
        }

        private static object? ReadColumnValue(ByteChunk data, DbColumnSchema column)
        {
            if (column.Type == DbTypesEnum.List || column.Type == DbTypesEnum.StringLookup)
                throw new NotSupportedException($"Unsupported DB field type: {column.Type}");

            try
            {
                var parser = ByteParsers.GetParser(column.Type);
                var value = parser.GetValueAsObject(data.Buffer, data.Index, out var bytesRead);
                data.Advance(bytesRead);
                return value;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Failed to decode column '{column.Name}' ({column.Type}) at byte index {data.Index}. {ex.Message}", ex);
            }
        }
    }
}
