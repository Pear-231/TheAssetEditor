namespace Shared.GameFormats.DB
{
    public class DbSchemaFile
    {
        public Dictionary<string, List<DbTableSchema>> TableSchemas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
