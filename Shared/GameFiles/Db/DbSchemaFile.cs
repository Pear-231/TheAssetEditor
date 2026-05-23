namespace Shared.GameFormats.Db
{
    public class DbSchemaFile
    {
        public Dictionary<string, List<DbTableSchema>> TableSchemas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
