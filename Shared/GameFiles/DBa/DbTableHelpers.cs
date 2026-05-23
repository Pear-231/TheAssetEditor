namespace Shared.GameFormats.DB
{
    public static class DbTableHelpers
    {
        public static string NormaliseLookupTableFolder(string tableName)
        {
            var normalised = tableName.Replace('/', '\\').Trim();
            if (normalised.StartsWith("db\\", StringComparison.OrdinalIgnoreCase))
            {
                var split = normalised.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (split.Length >= 2)
                    normalised = split[1];
            }

            normalised = normalised.Trim('\\');
            if (normalised.EndsWith("_tables", StringComparison.OrdinalIgnoreCase))
                return normalised;

            return normalised + "_tables";
        }

        public static string NormaliseSchemaTableName(string tableName)
        {
            var output = tableName.Trim();
            if (output.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                output = output[..^4];

            return output;
        }
    }
}
