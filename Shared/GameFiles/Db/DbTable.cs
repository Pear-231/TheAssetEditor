using Shared.ByteParsing;
using Shared.ByteParsing.Parsers;
using Shared.Core.PackFiles.Models;
using System.Text;

namespace Shared.GameFormats.Db
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

        public static DbTable CreateFromBytesAtOffsets(byte[] fileContent, string tableName, DbTableSchema schema, IReadOnlyList<int> rowOffsets)
        {
            if (fileContent == null)
                throw new ArgumentNullException(nameof(fileContent));
            if (schema == null)
                throw new ArgumentNullException(nameof(schema));
            if (rowOffsets == null)
                throw new ArgumentNullException(nameof(rowOffsets));

            var table = new DbTable
            {
                TableName = tableName,
                Schema = schema
            };

            table.ReadDataAtOffsets(fileContent, rowOffsets);
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

                try
                {
                    foreach (var column in Schema.ColumnSchemas)
                        row.Values[column.Name] = ReadColumnValue(data, column);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to decode row {rowIndex} in table '{TableName}'. {ex.Message}", ex);
                }

                rows.Add(row);
            }

            Header = header;
            Rows = rows;
        }

        public void ReadDataAtOffsets(byte[] fileContent, IReadOnlyList<int> rowOffsets)
        {
            if (fileContent == null)
                throw new ArgumentNullException(nameof(fileContent));
            if (rowOffsets == null)
                throw new ArgumentNullException(nameof(rowOffsets));

            if (Schema == null)
                throw new ArgumentNullException(nameof(Schema));

            var data = new ByteChunk(fileContent);
            var header = DbTableHeader.ReadData(data);

            var rows = new List<DbTableRow>();
            foreach (var rowOffset in rowOffsets.OrderBy(x => x))
            {
                if (rowOffset < data.Index || rowOffset >= fileContent.Length)
                    continue;

                var rowData = new ByteChunk(fileContent, rowOffset);
                var row = new DbTableRow();

                try
                {
                    foreach (var column in Schema.ColumnSchemas)
                        row.Values[column.Name] = ReadColumnValue(rowData, column);
                }
                catch
                {
                    continue;
                }

                rows.Add(row);
            }

            Header = header;
            Rows = rows;
        }

        private static object? ReadColumnValue(ByteChunk data, DbColumnSchema column)
        {
            if (column.StringSerialisationMode == DbStringSerialisationMode.FixedLengthZeroTerminatedUtf8
                && (column.Type == DbTypesEnum.String
                    || column.Type == DbTypesEnum.String_ascii
                    || column.Type == DbTypesEnum.Optstring
                    || column.Type == DbTypesEnum.Optstring_ascii))
            {
                if (column.MaxLength <= 0)
                    throw new Exception($"Column '{column.Name}' uses fixed-length text serialisation but has an invalid MaxLength of {column.MaxLength}.");

                if (data.BytesLeft < column.MaxLength)
                    throw new Exception($"Failed to decode fixed-length text column '{column.Name}'. Needs {column.MaxLength} bytes but only {data.BytesLeft} bytes remain.");

                var rawValue = Encoding.UTF8.GetString(data.Buffer, data.Index, column.MaxLength);
                var zeroTerminatorIndex = rawValue.IndexOf('\0');
                var value = zeroTerminatorIndex >= 0
                    ? rawValue.Substring(0, zeroTerminatorIndex)
                    : rawValue;

                data.Advance(column.MaxLength);
                return value;
            }

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

            if (column.Type == DbTypesEnum.Optstring || column.Type == DbTypesEnum.Optstring_ascii)
            {
                var optStringFlag = data.Buffer[data.Index];
                if (optStringFlag > 1)
                {
                    var fallbackType = column.Type == DbTypesEnum.Optstring ? DbTypesEnum.String : DbTypesEnum.String_ascii;

                    try
                    {
                        var fallbackParser = ByteParsers.GetParser(fallbackType);
                        var fallbackValue = fallbackParser.GetValueAsObject(data.Buffer, data.Index, out var fallbackBytesRead);
                        data.Advance(fallbackBytesRead);
                        return fallbackValue;
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to decode column '{column.Name}' ({column.Type}) at byte index {data.Index}. Optional string flag byte was {optStringFlag}, so it was interpreted as a non-optional string, but fallback parsing failed. {ex.Message}", ex);
                    }
                }
            }

            if (column.IsOptional && column.Type != DbTypesEnum.Optstring && column.Type != DbTypesEnum.Optstring_ascii)
            {
                if (data.BytesLeft < 1)
                    throw new Exception($"Failed to decode optional column '{column.Name}' ({column.Type}) at byte index {data.Index}. No bytes left for optional flag.");

                var optionalFlag = data.Buffer[data.Index];
                if (optionalFlag <= 1)
                {
                    var hasValue = data.ReadBool();
                    object? optionalValue;
                    int optionalBytesRead;

                    try
                    {
                        var parser = ByteParsers.GetParser(column.Type);
                        optionalValue = parser.GetValueAsObject(data.Buffer, data.Index, out optionalBytesRead);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to decode optional column '{column.Name}' ({column.Type}) at byte index {data.Index}. {ex.Message}", ex);
                    }

                    data.Advance(optionalBytesRead);
                    if (!hasValue)
                        return null;

                    return optionalValue;
                }
            }

            object? requiredValue;
            int requiredBytesRead;

            try
            {
                var parserForRequiredValue = ByteParsers.GetParser(column.Type);
                requiredValue = parserForRequiredValue.GetValueAsObject(data.Buffer, data.Index, out requiredBytesRead);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to decode column '{column.Name}' ({column.Type}) at byte index {data.Index}. {ex.Message}", ex);
            }

            data.Advance(requiredBytesRead);
            return requiredValue;
        }
    }
}
