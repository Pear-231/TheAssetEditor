using System.Numerics;
using System.Text;

namespace Shared.GameFormats.CompressedMap
{
    public static class CompressedMapParser
    {
        private const string Magic = "FASTBIN0";
        private const string TableIndexedCodec = "TABLE_INDEXED";
        private const int MaximumDimension = 32768;
        private const int MaximumBlockSampleCount = 4096;

        public static CompressedMapFile Parse(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);

            using var stream = new MemoryStream(data, false);
            using var reader = new BinaryReader(stream, Encoding.ASCII, true);

            RequireRemaining(stream, 52, "compressed-map header");
            var magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
            if (magic != Magic)
                throw new InvalidDataException($"Expected {Magic} magic, got '{magic}'.");

            var version = reader.ReadUInt16();
            if (version != 3)
                throw new NotSupportedException($"Unsupported compressed-map version {version}.");

            var width = ReadDimension(reader, "width");
            var height = ReadDimension(reader, "height");
            var blockWidth = ReadDimension(reader, "block width");
            var blockHeight = ReadDimension(reader, "block height");
            var blockSampleCount = checked(blockWidth * blockHeight);
            if (blockSampleCount > MaximumBlockSampleCount)
                throw new InvalidDataException($"Block contains {blockSampleCount} samples; maximum supported is {MaximumBlockSampleCount}.");

            var unknownHeaderData = ReadExact(reader, 16, "unknown header data");
            var valueMaximum = reader.ReadSingle();
            var valueMinimum = reader.ReadSingle();
            if (!float.IsFinite(valueMinimum) || !float.IsFinite(valueMaximum) || valueMaximum < valueMinimum)
                throw new InvalidDataException($"Invalid value range {valueMinimum}..{valueMaximum}.");

            var codecNameLength = reader.ReadUInt16();
            var codecName = Encoding.ASCII.GetString(ReadExact(reader, codecNameLength, "codec name"));
            if (codecName != TableIndexedCodec)
                throw new NotSupportedException($"Unsupported compressed-map codec '{codecName}'.");

            var expectedBlockColumns = DivideRoundUp(width, blockWidth);
            var expectedBlockRows = DivideRoundUp(height, blockHeight);
            var expectedBlockCount = checked(expectedBlockColumns * expectedBlockRows);
            var blockCount = ReadCount(reader, expectedBlockCount, "offset table");

            var offsets = new uint[blockCount];
            for (var i = 0; i < offsets.Length; i++)
                offsets[i] = ReadUInt32(reader, stream, $"block offset {i}");

            var lengthCount = ReadCount(reader, expectedBlockCount, "length table");
            var lengths = new ushort[lengthCount];
            for (var i = 0; i < lengths.Length; i++)
                lengths[i] = ReadUInt16(reader, stream, $"block length {i}");

            var payloadLength = ReadUInt32(reader, stream, "payload length");
            if (payloadLength != stream.Length - stream.Position)
                throw new InvalidDataException($"Declared payload length {payloadLength} does not match remaining length {stream.Length - stream.Position}.");

            ValidateBlockBounds(offsets, lengths, payloadLength);

            var payloadStart = stream.Position;
            var samples = new ushort[width, height];
            for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                stream.Position = checked(payloadStart + offsets[blockIndex]);
                var blockData = ReadExact(reader, lengths[blockIndex], $"block {blockIndex}");
                var blockSamples = DecodeBlock(blockData, blockSampleCount, blockIndex);

                var blockX = blockIndex % expectedBlockColumns;
                var blockY = blockIndex / expectedBlockColumns;
                CopyBlock(samples, width, height, blockSamples, blockX, blockY, blockWidth, blockHeight);
            }

            return new CompressedMapFile
            {
                Version = version,
                Width = width,
                Height = height,
                BlockWidth = blockWidth,
                BlockHeight = blockHeight,
                UnknownHeaderData = unknownHeaderData,
                ValueMaximum = valueMaximum,
                ValueMinimum = valueMinimum,
                CodecName = codecName,
                Samples = samples
            };
        }

        private static ushort[] DecodeBlock(byte[] data, int sampleCount, int blockIndex)
        {
            if (data.Length == 0)
                throw new InvalidDataException($"Block {blockIndex} is empty.");

            var mode = data[0];
            if (mode <= 0x7f)
                return DecodeTableBlock(data, sampleCount, mode, blockIndex);

            if (mode == 0x8f)
                return DecodeRawBlock(data, sampleCount, blockIndex);

            if (mode is >= 0x80 and <= 0x8e)
                return DecodeRangeBlock(data, sampleCount, mode, blockIndex);

            throw new InvalidDataException($"Block {blockIndex} uses unsupported mode 0x{mode:X2}.");
        }

        private static ushort[] DecodeTableBlock(byte[] data, int sampleCount, byte mode, int blockIndex)
        {
            var tableCount = mode + 1;
            var bitsPerIndex = BitOperations.Log2((uint)(tableCount - 1)) + 1;
            if (tableCount == 1)
                bitsPerIndex = 0;

            var packedByteCount = DivideRoundUp(checked(sampleCount * bitsPerIndex), 8);
            var paddingByteCount = bitsPerIndex == 0 ? 0 : 3;
            var expectedLength = checked(1 + (tableCount * sizeof(ushort)) + packedByteCount + paddingByteCount);
            RequireBlockLength(data, expectedLength, blockIndex, mode);

            var table = new ushort[tableCount];
            var cursor = 1;
            for (var i = 0; i < table.Length; i++, cursor += sizeof(ushort))
                table[i] = BitConverter.ToUInt16(data, cursor);

            var indices = ReadPackedValues(data.AsSpan(cursor, packedByteCount), sampleCount, bitsPerIndex, blockIndex);
            var result = new ushort[sampleCount];
            for (var i = 0; i < result.Length; i++)
            {
                if (indices[i] >= table.Length)
                    throw new InvalidDataException($"Block {blockIndex} index {indices[i]} exceeds table size {table.Length}.");
                result[i] = table[indices[i]];
            }

            return result;
        }

        private static ushort[] DecodeRangeBlock(byte[] data, int sampleCount, byte mode, int blockIndex)
        {
            var bitsPerOffset = (mode & 0x0f) + 1;
            var packedByteCount = DivideRoundUp(checked(sampleCount * bitsPerOffset), 8);
            var expectedLength = checked(1 + sizeof(ushort) + packedByteCount + 3);
            RequireBlockLength(data, expectedLength, blockIndex, mode);

            var minimum = BitConverter.ToUInt16(data, 1);
            var offsets = ReadPackedValues(data.AsSpan(3, packedByteCount), sampleCount, bitsPerOffset, blockIndex);
            var result = new ushort[sampleCount];
            for (var i = 0; i < result.Length; i++)
            {
                var value = minimum + offsets[i];
                if (value > ushort.MaxValue)
                    throw new InvalidDataException($"Block {blockIndex} sample {i} exceeds UInt16 range.");
                result[i] = (ushort)value;
            }

            return result;
        }

        private static ushort[] DecodeRawBlock(byte[] data, int sampleCount, int blockIndex)
        {
            var expectedLength = checked(1 + (sampleCount * sizeof(ushort)));
            RequireBlockLength(data, expectedLength, blockIndex, 0x8f);

            var result = new ushort[sampleCount];
            for (var i = 0; i < result.Length; i++)
                result[i] = BitConverter.ToUInt16(data, 1 + (i * sizeof(ushort)));
            return result;
        }

        private static int[] ReadPackedValues(ReadOnlySpan<byte> data, int valueCount, int bitsPerValue, int blockIndex)
        {
            var result = new int[valueCount];
            if (bitsPerValue == 0)
                return result;

            ulong accumulator = 0;
            var bitsInAccumulator = 0;
            var sourceIndex = 0;
            var mask = (1UL << bitsPerValue) - 1;

            for (var i = 0; i < valueCount; i++)
            {
                while (bitsInAccumulator < bitsPerValue)
                {
                    if (sourceIndex >= data.Length)
                        throw new InvalidDataException($"Block {blockIndex} packed values ended at sample {i}.");
                    accumulator |= (ulong)data[sourceIndex++] << bitsInAccumulator;
                    bitsInAccumulator += 8;
                }

                result[i] = (int)(accumulator & mask);
                accumulator >>= bitsPerValue;
                bitsInAccumulator -= bitsPerValue;
            }

            return result;
        }

        private static void CopyBlock(
            ushort[,] destination,
            int width,
            int height,
            ushort[] blockSamples,
            int blockX,
            int blockY,
            int blockWidth,
            int blockHeight)
        {
            for (var localY = 0; localY < blockHeight; localY++)
            {
                var y = (blockY * blockHeight) + localY;
                if (y >= height)
                    break;

                for (var localX = 0; localX < blockWidth; localX++)
                {
                    var x = (blockX * blockWidth) + localX;
                    if (x >= width)
                        break;
                    destination[x, y] = blockSamples[(localY * blockWidth) + localX];
                }
            }
        }

        private static void ValidateBlockBounds(uint[] offsets, ushort[] lengths, uint payloadLength)
        {
            for (var i = 0; i < offsets.Length; i++)
            {
                if (i == 0 && offsets[i] != 0)
                    throw new InvalidDataException($"First block offset is {offsets[i]}, expected 0.");
                if (i > 0 && offsets[i] != offsets[i - 1] + lengths[i - 1])
                    throw new InvalidDataException($"Block {i} offset {offsets[i]} does not follow the previous block.");
                if ((ulong)offsets[i] + lengths[i] > payloadLength)
                    throw new InvalidDataException($"Block {i} extends beyond the payload.");
            }

            if (offsets.Length > 0 && (ulong)offsets[^1] + lengths[^1] != payloadLength)
                throw new InvalidDataException("The final block does not end at the declared payload boundary.");
        }

        private static int ReadDimension(BinaryReader reader, string name)
        {
            var value = reader.ReadUInt32();
            if (value == 0 || value > MaximumDimension)
                throw new InvalidDataException($"Invalid {name} {value}.");
            return checked((int)value);
        }

        private static int ReadCount(BinaryReader reader, int expectedCount, string name)
        {
            var count = ReadUInt32(reader, reader.BaseStream, name);
            if (count != expectedCount)
                throw new InvalidDataException($"{name} count {count} does not match expected block count {expectedCount}.");
            return checked((int)count);
        }

        private static byte[] ReadExact(BinaryReader reader, int count, string context)
        {
            RequireRemaining(reader.BaseStream, count, context);
            var data = reader.ReadBytes(count);
            if (data.Length != count)
                throw new EndOfStreamException($"Expected {count} bytes for {context}, got {data.Length}.");
            return data;
        }

        private static ushort ReadUInt16(BinaryReader reader, Stream stream, string context)
        {
            RequireRemaining(stream, sizeof(ushort), context);
            return reader.ReadUInt16();
        }

        private static uint ReadUInt32(BinaryReader reader, Stream stream, string context)
        {
            RequireRemaining(stream, sizeof(uint), context);
            return reader.ReadUInt32();
        }

        private static void RequireRemaining(Stream stream, long count, string context)
        {
            if (count < 0 || stream.Position > stream.Length - count)
                throw new EndOfStreamException($"Not enough data for {context} at offset {stream.Position}.");
        }

        private static void RequireBlockLength(byte[] data, int expectedLength, int blockIndex, byte mode)
        {
            if (data.Length != expectedLength)
                throw new InvalidDataException($"Block {blockIndex} mode 0x{mode:X2} has length {data.Length}, expected {expectedLength}.");
        }

        private static int DivideRoundUp(int value, int divisor) => checked((value + divisor - 1) / divisor);
    }
}
