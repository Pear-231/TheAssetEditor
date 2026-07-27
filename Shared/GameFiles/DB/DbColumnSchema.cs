using Shared.ByteParsing.Parsers;

namespace Shared.GameFormats.DB
{
    public class DbColumnSchema
    {
        public string Name { get; set; } = string.Empty;
        public string FieldReference { get; set; } = string.Empty;
        public string TableReference { get; set; } = string.Empty;
        public bool IsKey { get; set; }
        public bool IsOptional { get; set; }
        public int MaxLength { get; set; }
        public bool IsFileName { get; set; }
        public string Description { get; set; } = string.Empty;
        public string FilenameRelativePath { get; set; } = string.Empty;
        public DbTypesEnum Type { get; set; }
    }
}
