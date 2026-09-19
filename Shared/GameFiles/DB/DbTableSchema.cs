namespace Shared.GameFormats.DB
{
    public class DbTableSchema
    {
        public string TableName { get; set; } = string.Empty;
        public int Version { get; set; }
        public List<DbColumnSchema> ColumnSchemas { get; set; } = [];
    }
}
