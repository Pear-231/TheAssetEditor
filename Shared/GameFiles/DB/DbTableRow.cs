namespace Shared.GameFormats.DB
{
    public class DbTableRow
    {
        public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public object? this[string columnName] => GetValue(columnName);

        public object? GetValue(string columnName)
        {
            Values.TryGetValue(columnName, out var value);
            return value;
        }

        public string? GetString(string columnName)
        {
            var value = GetValue(columnName);
            if (value == null)
                return null;

            return Convert.ToString(value);
        }
    }
}
