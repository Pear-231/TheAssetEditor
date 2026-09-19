using Shared.ByteParsing;
using Shared.GameFormats.Audio.Formats.Pcm;

namespace Shared.GameFormats.Audio.Containers.Wav
{
    public class FmtChunk : RiffChunk
    {
        public const int ChunkSize = 16;
        public const string ChunkTag = "fmt ";
        public const ushort PcmFormatTag = 1;
        public const ushort IeeeFloatFormatTag = 3;
        public const ushort ExtensibleFormatTag = 0xFFFE;

        private static readonly Guid s_pcmSubFormat = new(PcmFormatTag, 0, 0x0010, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71);
        private static readonly Guid s_ieeeFloatSubFormat = new(IeeeFloatFormatTag, 0, 0x0010, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71);

        public int Size { get; set; } = ChunkSize;
        public ushort FormatTag { get; set; } = PcmFormatTag;
        public ushort Channels { get; set; }
        public uint SampleRate { get; set; }
        public uint ByteRate { get; set; }
        public ushort BlockAlign { get; set; }
        public ushort BitsPerSample { get; set; }
        public ushort ExtensionSize { get; private set; }
        public ushort ValidBitsPerSample { get; private set; }
        public uint ChannelMask { get; private set; }
        public Guid SubFormat { get; private set; }

        public FmtChunk()
        {
            Tag = ChunkTag;
        }

        public override void ReadData(ByteChunk chunk)
        {
            if (chunk.BytesLeft < ChunkSize)
                throw new InvalidDataException($"WAV fmt chunk must be at least {ChunkSize} bytes.");

            FormatTag = chunk.ReadUShort();
            Channels = chunk.ReadUShort();
            SampleRate = chunk.ReadUInt32();
            ByteRate = chunk.ReadUInt32();
            BlockAlign = chunk.ReadUShort();
            BitsPerSample = chunk.ReadUShort();

            if (chunk.BytesLeft >= sizeof(ushort))
            {
                ExtensionSize = chunk.ReadUShort();
                if (ExtensionSize > chunk.BytesLeft)
                    throw new InvalidDataException("WAV fmt extension extends beyond the fmt chunk.");
            }

            if (FormatTag == ExtensibleFormatTag)
            {
                if (ExtensionSize < 22 || chunk.BytesLeft < 22)
                    throw new InvalidDataException("WAVE_FORMAT_EXTENSIBLE requires a 22-byte fmt extension.");

                ValidBitsPerSample = chunk.ReadUShort();
                ChannelMask = chunk.ReadUInt32();
                SubFormat = new Guid(chunk.ReadBytes(16));
            }
        }

        public void Validate()
        {
            if (Channels == 0 || SampleRate == 0 || BitsPerSample == 0)
                throw new InvalidDataException("WAV fmt chunk has zero channels, sample rate or bits per sample.");

            var sampleFormat = GetFormatType();
            if (sampleFormat == SampleFormat.Float && BitsPerSample != 32)
                throw new InvalidDataException("Only 32-bit IEEE float WAV data is supported.");
            if (sampleFormat == SampleFormat.Integer && BitsPerSample is not (8 or 16 or 24 or 32))
                throw new InvalidDataException("Only 8-bit, 16-bit, 24-bit and 32-bit PCM WAV data is supported.");
            if (FormatTag == ExtensibleFormatTag && ValidBitsPerSample != BitsPerSample)
                throw new InvalidDataException("WAVE_FORMAT_EXTENSIBLE valid bits must match the sample container size.");

            var expectedBlockAlign = checked((ushort)(Channels * (BitsPerSample / 8)));
            var expectedByteRate = checked(SampleRate * expectedBlockAlign);
            if (BlockAlign != expectedBlockAlign || ByteRate != expectedByteRate)
                throw new InvalidDataException("WAV fmt block alignment or byte rate is inconsistent with its sample format.");
        }

        public override byte[] WriteData()
        {
            if (FormatTag == ExtensibleFormatTag)
                throw new NotSupportedException("Writing WAVE_FORMAT_EXTENSIBLE fmt chunks is not supported.");

            using var stream = new MemoryStream(ChunkSize);
            stream.Write(ByteParsers.UShort.EncodeValue(FormatTag, out _));
            stream.Write(ByteParsers.UShort.EncodeValue(Channels, out _));
            stream.Write(ByteParsers.UInt32.EncodeValue(SampleRate, out _));
            stream.Write(ByteParsers.UInt32.EncodeValue(ByteRate, out _));
            stream.Write(ByteParsers.UShort.EncodeValue(BlockAlign, out _));
            stream.Write(ByteParsers.UShort.EncodeValue(BitsPerSample, out _));
            return stream.ToArray();
        }

        public SampleFormat GetFormatType()
        {
            if (FormatTag == PcmFormatTag)
                return SampleFormat.Integer;

            if (FormatTag == IeeeFloatFormatTag)
                return SampleFormat.Float;

            if (FormatTag != ExtensibleFormatTag)
                throw new InvalidDataException($"Unsupported WAV format tag: 0x{FormatTag:X4}.");

            if (SubFormat == s_pcmSubFormat)
                return SampleFormat.Integer;

            if (SubFormat == s_ieeeFloatSubFormat)
                return SampleFormat.Float;

            throw new InvalidDataException($"Unsupported WAVE_FORMAT_EXTENSIBLE subformat: {SubFormat}.");
        }

        public static ushort GetFormatTag(SampleFormat sampleFormat) => sampleFormat == SampleFormat.Float ? IeeeFloatFormatTag : PcmFormatTag;
    }
}
