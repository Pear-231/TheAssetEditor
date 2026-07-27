namespace Shared.ByteParsing.Parsers
{
    public class OptionalIntParser : NumberParser<int>
    {
        public override string TypeName => "OptionalInt32";
        public override DbTypesEnum Type => DbTypesEnum.OptionalInteger;
        protected override int FieldSize => 5;

        protected override int Decode(byte[] buffer, int index)
        {
            return BitConverter.ToInt32(buffer, index + 1);
        }

        public override bool CanDecode(byte[] buffer, int index, out int bytesRead, out string? error)
        {
            if (!base.CanDecode(buffer, index, out bytesRead, out error))
                return false;

            var flag = buffer[index];
            if (flag is 0 or 1)
                return true;

            bytesRead = 0;
            error = $"{flag} is not a valid optional integer flag";
            return false;
        }

        public override byte[]? EncodeValue(int value, out string? error)
        {
            error = null;
            return [1, .. BitConverter.GetBytes(value)];
        }

        public override byte[]? Encode(string value, out string? error)
        {
            if (!int.TryParse(value, out var parsedValue))
            {
                error = "Unable to convert string to value";
                return null;
            }

            return EncodeValue(parsedValue, out error);
        }
    }
}
