using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise
{
    public static class WwiseVariableUInt32Parser
    {
        public static uint Read(ByteChunk chunk)
        {
            uint value = 0;
            for (var index = 0; index < 5; index++)
            {
                var current = chunk.ReadByte();
                if (value > (uint.MaxValue >> 7))
                    throw new InvalidDataException("Wwise variable-length integer exceeds 32 bits.");

                value = (value << 7) | (uint)(current & 0x7F);
                if ((current & 0x80) == 0)
                    return value;
            }

            throw new InvalidDataException("Wwise variable-length integer exceeds five bytes.");
        }

        public static byte[] Encode(uint value)
        {
            Span<byte> groups = stackalloc byte[5];
            var index = groups.Length;
            groups[--index] = (byte)(value & 0x7F);
            value >>= 7;

            while (value != 0)
            {
                groups[--index] = (byte)((value & 0x7F) | 0x80);
                value >>= 7;
            }

            return groups[index..].ToArray();
        }

        public static uint GetSize(uint value)
        {
            uint size = 1;
            while ((value >>= 7) != 0)
                size++;
            return size;
        }
    }
}
