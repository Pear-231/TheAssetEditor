namespace Shared.GameFormats.Db
{
    public class DbTableSchema
    {
        public string TableName { get; set; } = string.Empty;
        public int Version { get; set; }
        public List<DbColumnSchema> ColumnSchemas { get; set; } = [];

        public DbTableSchema DeepClone()
        {
            var clonedColumns = new List<DbColumnSchema>(ColumnSchemas.Count);
            foreach (var columnSchema in ColumnSchemas)
                clonedColumns.Add(columnSchema.DeepClone());

            return new DbTableSchema
            {
                TableName = TableName,
                Version = Version,
                ColumnSchemas = clonedColumns
            };
        }
    }
}
