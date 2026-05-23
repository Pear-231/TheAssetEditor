using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Shared.ByteParsing.Parsers;

namespace Shared.GameFormats.DB
{
    public class DbColumnSchema
    {
        public string Name { get; set; } = string.Empty;
        public string FieldReference { get; set; } = string.Empty;
        public string TableReference { get; set; } = string.Empty;
        public bool IsKey { get; set; } = false;
        public bool IsOptional { get; set; }
        public int MaxLength { get; set; }
        public bool IsFileName { get; set; } = false;
        public string Description { get; set; } = string.Empty;
        public string FilenameRelativePath { get; set; } = string.Empty;

        [JsonConverter(typeof(StringEnumConverter))]
        public DbTypesEnum Type { get; set; }

        public DbColumnSchema DeepClone()
        {
            return (DbColumnSchema)MemberwiseClone();
        }
    }
}
