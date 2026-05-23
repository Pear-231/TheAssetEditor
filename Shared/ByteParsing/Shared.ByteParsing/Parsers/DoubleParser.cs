namespace Shared.ByteParsing.Parsers
{
    public class DoubleParser : NumberParser<double>
    {
        public override string TypeName { get { return "Double"; } }
        public override DbTypesEnum Type => DbTypesEnum.Double;
        protected override int FieldSize => 8;

        protected override double Decode(byte[] buffer, int index)
        {
            return BitConverter.ToDouble(buffer, index);
        }

        public override bool TryDecode(byte[] buffer, int index, out string value, out int bytesRead, out string? error)
        {
            var result = TryDecodeValue(buffer, index, out var temp, out bytesRead, out error);
            value = temp.ToString("0.0000000000000000");
            return result;
        }

        public override byte[]? EncodeValue(double value, out string? error)
        {
            error = null;
            return BitConverter.GetBytes(value);
        }

        public override byte[]? Encode(string value, out string? error)
        {
            if (!double.TryParse(value, out var specificValue))
            {
                error = "Unable to convert string to value";
                return null;
            }

            return EncodeValue(specificValue, out error);
        }
    }
}
