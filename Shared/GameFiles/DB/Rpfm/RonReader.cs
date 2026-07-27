using System.Globalization;
using System.Text;
using Shared.ByteParsing.Parsers;

namespace Shared.GameFormats.DB.Rpfm
{
    internal sealed class RonReader(string text)
    {
        private readonly string _text = text;
        private int _index;

        public Dictionary<string, List<DbTableSchema>> ReadTableSchemas()
        {
            var definitionsIndex = _text.IndexOf("definitions:", StringComparison.Ordinal);
            if (definitionsIndex < 0)
                throw new InvalidDataException("The RPFM schema has no definitions map.");

            _index = definitionsIndex + "definitions:".Length;
            Expect('{');

            var result = new Dictionary<string, List<DbTableSchema>>(StringComparer.OrdinalIgnoreCase);
            while (!TryTake('}'))
            {
                var tableName = ReadString();
                Expect(':');
                var definitions = ReadList()
                    .Select(value => ConvertDefinition(tableName, AsObject(value)))
                    .ToList();
                result.Add(tableName, definitions);
                TryTake(',');
            }

            return result;
        }

        private static DbTableSchema ConvertDefinition(string tableName, Dictionary<string, object?> definition)
        {
            var fields = AsList(definition["fields"]);
            return new DbTableSchema
            {
                TableName = tableName,
                Version = Convert.ToInt32(definition["version"], CultureInfo.InvariantCulture),
                ColumnSchemas = fields
                    .Select(value => ConvertField(AsObject(value)))
                    .ToList()
            };
        }

        private static DbColumnSchema ConvertField(Dictionary<string, object?> field)
        {
            var rpfmType = Convert.ToString(field["field_type"], CultureInfo.InvariantCulture)
                ?? throw new InvalidDataException("An RPFM field has no type.");
            var reference = field.GetValueOrDefault("is_reference") as List<object?>;

            return new DbColumnSchema
            {
                Name = Convert.ToString(field["name"], CultureInfo.InvariantCulture) ?? string.Empty,
                Type = ConvertFieldType(rpfmType),
                IsKey = GetBool(field, "is_key"),
                IsOptional = rpfmType is "OptionalI32" or "OptionalStringU8",
                IsFileName = GetBool(field, "is_filename"),
                FilenameRelativePath = GetString(field, "filename_relative_path"),
                Description = GetString(field, "description"),
                TableReference = reference?.Count == 2 ? Convert.ToString(reference[0], CultureInfo.InvariantCulture) ?? string.Empty : string.Empty,
                FieldReference = reference?.Count == 2 ? Convert.ToString(reference[1], CultureInfo.InvariantCulture) ?? string.Empty : string.Empty
            };
        }

        private static DbTypesEnum ConvertFieldType(string fieldType)
        {
            return fieldType switch
            {
                "Boolean" => DbTypesEnum.Boolean,
                "ColourRGB" => DbTypesEnum.ColourRGB,
                "F32" => DbTypesEnum.Single,
                "F64" => DbTypesEnum.Double,
                "I16" => DbTypesEnum.Short,
                "I32" => DbTypesEnum.Integer,
                "I64" => DbTypesEnum.Int64,
                "OptionalI32" => DbTypesEnum.OptionalInteger,
                "OptionalStringU8" => DbTypesEnum.Optstring,
                "StringU16" => DbTypesEnum.String_ascii,
                "StringU8" => DbTypesEnum.String,
                _ => throw new NotSupportedException($"Unsupported RPFM DB field type '{fieldType}'.")
            };
        }

        private object? ReadValue()
        {
            SkipWhitespace();
            var next = Peek();
            if (next == '"')
                return ReadString();
            if (next == '(')
                return ReadTuple();
            if (next == '[')
                return ReadList();
            if (next == '{')
                return ReadMap();
            if (next == '-' || char.IsDigit(next))
                return ReadInteger();

            var identifier = ReadIdentifier();
            if (identifier == "None")
                return null;
            if (identifier == "true")
                return true;
            if (identifier == "false")
                return false;

            SkipWhitespace();
            if (Peek() == '(')
            {
                var value = ReadTuple();
                return identifier == "Some"
                    ? value
                    : new Dictionary<string, object?> { ["variant"] = identifier, ["value"] = value };
            }

            return identifier;
        }

        private object ReadTuple()
        {
            Expect('(');
            if (TryTake(')'))
                return new Dictionary<string, object?>();

            var savedIndex = _index;
            SkipWhitespace();
            var isObject = char.IsLetter(Peek()) || Peek() == '_';
            if (isObject)
            {
                _ = ReadIdentifier();
                SkipWhitespace();
                isObject = Peek() == ':';
            }
            _index = savedIndex;

            if (isObject)
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                while (!TryTake(')'))
                {
                    var name = ReadIdentifier();
                    Expect(':');
                    result[name] = ReadValue();
                    TryTake(',');
                }
                return result;
            }

            var values = new List<object?>();
            while (!TryTake(')'))
            {
                values.Add(ReadValue());
                TryTake(',');
            }
            return values.Count == 1 ? values[0]! : values;
        }

        private List<object?> ReadList()
        {
            Expect('[');
            var result = new List<object?>();
            while (!TryTake(']'))
            {
                result.Add(ReadValue());
                TryTake(',');
            }
            return result;
        }

        private Dictionary<string, object?> ReadMap()
        {
            Expect('{');
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            while (!TryTake('}'))
            {
                var key = Convert.ToString(ReadValue(), CultureInfo.InvariantCulture) ?? string.Empty;
                Expect(':');
                result[key] = ReadValue();
                TryTake(',');
            }
            return result;
        }

        private string ReadString()
        {
            SkipWhitespace();
            ExpectRaw('"');
            var result = new StringBuilder();

            while (_index < _text.Length)
            {
                var value = _text[_index++];
                if (value == '"')
                    return result.ToString();
                if (value != '\\')
                {
                    result.Append(value);
                    continue;
                }

                var escaped = _text[_index++];
                result.Append(escaped switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '0' => '\0',
                    'x' => ReadHexCharacter(2),
                    'u' => ReadUnicodeCharacter(),
                    _ => escaped
                });
            }

            throw new InvalidDataException("Unterminated string in RPFM schema.");
        }

        private char ReadHexCharacter(int digits)
        {
            var value = Convert.ToInt32(_text.Substring(_index, digits), 16);
            _index += digits;
            return (char)value;
        }

        private string ReadUnicodeCharacter()
        {
            ExpectRaw('{');
            var end = _text.IndexOf('}', _index);
            if (end < 0)
                throw new InvalidDataException("Unterminated unicode escape in RPFM schema.");
            var codePoint = Convert.ToInt32(_text[_index..end], 16);
            _index = end + 1;
            return char.ConvertFromUtf32(codePoint);
        }

        private long ReadInteger()
        {
            SkipWhitespace();
            var start = _index;
            if (Peek() == '-')
                _index++;
            while (_index < _text.Length && char.IsDigit(_text[_index]))
                _index++;
            return long.Parse(_text[start.._index], CultureInfo.InvariantCulture);
        }

        private string ReadIdentifier()
        {
            SkipWhitespace();
            var start = _index;
            while (_index < _text.Length && (char.IsLetterOrDigit(_text[_index]) || _text[_index] == '_'))
                _index++;
            if (start == _index)
                throw new InvalidDataException($"Expected an identifier at RON character {_index}.");
            return _text[start.._index];
        }

        private void Expect(char expected)
        {
            SkipWhitespace();
            ExpectRaw(expected);
        }

        private void ExpectRaw(char expected)
        {
            if (_index >= _text.Length || _text[_index] != expected)
                throw new InvalidDataException($"Expected '{expected}' at RON character {_index}.");
            _index++;
        }

        private bool TryTake(char value)
        {
            SkipWhitespace();
            if (Peek() != value)
                return false;
            _index++;
            return true;
        }

        private char Peek()
        {
            return _index < _text.Length ? _text[_index] : '\0';
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                _index++;
        }

        private static Dictionary<string, object?> AsObject(object? value)
        {
            return value as Dictionary<string, object?>
                ?? throw new InvalidDataException("Expected an RPFM RON object.");
        }

        private static List<object?> AsList(object? value)
        {
            return value as List<object?>
                ?? throw new InvalidDataException("Expected an RPFM RON list.");
        }

        private static bool GetBool(Dictionary<string, object?> value, string key)
        {
            return value.GetValueOrDefault(key) is true;
        }

        private static string GetString(Dictionary<string, object?> value, string key)
        {
            return Convert.ToString(value.GetValueOrDefault(key), CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
