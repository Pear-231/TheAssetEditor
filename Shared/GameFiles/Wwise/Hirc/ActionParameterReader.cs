using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;

namespace Shared.GameFormats.Wwise.Hirc
{
    internal static class ActionParameterReader
    {
        public static byte[] Read(ByteChunk chunk, AkActionType actionType, uint version)
        {
            var start = chunk.Index;
            var family = (ushort)((ushort)actionType & 0xFF00);

            if (family == 0x0100) // Stop
                ReadActive(chunk, version, hasSpecificParameter: version >= 125);
            else if (family is 0x0200 or 0x0300) // Pause or resume
                ReadActive(chunk, version, hasSpecificParameter: true);
            else if (family == 0x2200) // Reset playlist
                ReadActive(chunk, version, hasSpecificParameter: false);
            else if (family is 0x0600 or 0x0700) // Mute or unmute
            {
                chunk.Advance(1);
                ReadExceptions(chunk, version);
            }
            else if (family is 0x0800 or 0x0900 or 0x0A00 or 0x0B00 or 0x0C00 or 0x0D00 or 0x0E00 or 0x0F00 or 0x2000 or 0x3000)
            {
                // Set/reset pitch, volume, bus volume, LPF or HPF.
                chunk.Advance(1 + 1 + 3 * sizeof(float));
                ReadExceptions(chunk, version);
            }
            else if (family is 0x1300 or 0x1400) // Set or reset game parameter
            {
                chunk.Advance(1);
                if (version > 89)
                    chunk.Advance(1);
                chunk.Advance(1 + 3 * sizeof(float));
                ReadExceptions(chunk, version);
            }
            else if (family is 0x1A00 or 0x1B00 or 0x3300 or 0x3400 or 0x3500 or 0x3600 or 0x3700)
            {
                // Bypass or reset bypass FX.
                chunk.Advance(2);
                ReadExceptions(chunk, version);
            }
            else if (family == 0x1E00) // Seek
            {
                chunk.Advance(1 + 3 * sizeof(float) + 1);
                ReadExceptions(chunk, version);
            }

            return chunk.GetBytesFromBuffer(start, chunk.Index - start);
        }

        private static void ReadActive(ByteChunk chunk, uint version, bool hasSpecificParameter)
        {
            chunk.Advance(1);
            if (hasSpecificParameter)
                chunk.Advance(1);
            ReadExceptions(chunk, version);
        }

        private static void ReadExceptions(ByteChunk chunk, uint version)
        {
            var count = version <= 122 ? chunk.ReadUInt32() : WwiseVariableUInt32Parser.Read(chunk);
            if (count > int.MaxValue / 5 || chunk.BytesLeft < count * 5)
                throw new InvalidDataException($"Wwise action exception list of {count} items exceeds its HIRC object.");
            chunk.Advance(checked((int)count * 5));
        }
    }
}
