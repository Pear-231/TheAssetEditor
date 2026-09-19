namespace Shared.ByteParsing.Parsers
{
    public class ColourRgbParser : NumberParser<uint>
    {
        public override string TypeName => "ColourRGB";
        public override DbTypesEnum Type => DbTypesEnum.ColourRGB;
        protected override int FieldSize => 4;

        protected override uint Decode(byte[] buffer, int index)
        {
            return BitConverter.ToUInt32(buffer, index);
        }

        public override byte[]? EncodeValue(uint value, out string? error)
        {
            error = null;
            return BitConverter.GetBytes(value);
        }

        public override byte[]? Encode(string value, out string? error)
        {
            var hexValue = value.TrimStart('#');
            if (!uint.TryParse(hexValue, System.Globalization.NumberStyles.HexNumber, null, out var parsedValue))
            {
                error = "Unable to convert string to an RGB colour";
                return null;
            }

            return EncodeValue(parsedValue, out error);
        }
    }
}
