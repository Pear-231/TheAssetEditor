using System.Runtime.InteropServices;
using NVorbis;
using Shared.ByteParsing;
using Shared.GameFormats.Audio.Containers.Ogg;

namespace Shared.GameFormats.Audio.Formats.Pcm
{
    public class PcmAudio
    {
        private const float MinPcmSampleValue = -1.0f;
        private const float MaxPcmSampleValue = 1.0f;
        private const int PcmBitsPerSample = 16;
        private const int FloatBitsPerSample = 32;
        private const int PcmSampleReadBufferSize = 4096;
        private const int Unsigned8BitMidpoint = 128;
        private const float Unsigned8BitFullScale = 128.0f;
        private const float Signed16BitFullScale = 32768.0f;
        private const float Signed24BitFullScale = 8388608.0f;
        private const float Signed32BitFullScale = 2147483648.0f;
        private const int SignBit24Bit = 0x800000;

        public ushort BitsPerSample { get; set; }
        public ushort Channels { get; set; }
        public byte[] Data { get; set; } = [];
        public uint SampleRate { get; set; }
        public SampleFormat SampleFormat { get; set; } = SampleFormat.Integer;
        public int SampleCount => Data.Length / (BitsPerSample / BitHelper.BitsPerByte) / Channels;

        public static PcmAudio CreateFromWemBytes(byte[] wemBytes)
        {
            var oggBytes = OggFile.CreateFromWemBytes(wemBytes).WriteData();
            return CreateFromOggBytes(oggBytes);
        }

        public static PcmAudio CreateFromOggBytes(byte[] oggData)
        {
            using var oggStream = new MemoryStream(oggData, writable: false);
            using var vorbisReader = new VorbisReader(oggStream, closeOnDispose: false);

            var channels = vorbisReader.Channels;
            var sampleRate = vorbisReader.SampleRate;
            var sampleReadBuffer = new float[PcmSampleReadBufferSize * channels];
            using var audioDataStream = new MemoryStream();

            int samplesRead;
            while ((samplesRead = vorbisReader.ReadSamples(sampleReadBuffer, 0, sampleReadBuffer.Length)) > 0)
            {
                for (var sampleIndex = 0; sampleIndex < samplesRead; sampleIndex++)
                {
                    var clampedSample = Math.Clamp(sampleReadBuffer[sampleIndex], MinPcmSampleValue, MaxPcmSampleValue);
                    var pcmSample = (short)Math.Round(clampedSample * short.MaxValue);
                    audioDataStream.WriteByte((byte)BitHelper.ExtractBits((uint)pcmSample, 0, BitHelper.BitsPerByte));
                    audioDataStream.WriteByte((byte)BitHelper.ExtractBits((uint)pcmSample, BitHelper.BitsPerByte, BitHelper.BitsPerByte));
                }
            }

            return new PcmAudio
            {
                BitsPerSample = PcmBitsPerSample,
                Channels = (ushort)channels,
                Data = audioDataStream.ToArray(),
                SampleRate = (uint)sampleRate,
            };
        }

        // The counterpart to ToInterleavedSamples. Float samples need no conversion,
        // so the buffer is reinterpreted rather than encoded.
        public static PcmAudio CreateFromFloatSamples(float[] interleavedSamples, ushort channels, uint sampleRate)
        {
            ArgumentNullException.ThrowIfNull(interleavedSamples);
            ArgumentOutOfRangeException.ThrowIfZero(channels);
            ArgumentOutOfRangeException.ThrowIfZero(sampleRate);

            return new PcmAudio
            {
                BitsPerSample = FloatBitsPerSample,
                Channels = channels,
                Data = MemoryMarshal.AsBytes(interleavedSamples.AsSpan()).ToArray(),
                SampleRate = sampleRate,
                SampleFormat = SampleFormat.Float
            };
        }

        // Samples in playback order, so frames are laid out one after another with
        // every channel of a frame side by side.
        public float[] ToInterleavedSamples()
        {
            var bytesPerSample = GetBytesPerSample();
            var samples = new float[Data.Length / bytesPerSample];
            for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                samples[sampleIndex] = ReadSample(sampleIndex * bytesPerSample);

            return samples;
        }

        // Samples split into one contiguous buffer per channel, which is what the
        // Vorbis encoder expects.
        public float[][] ToPerChannelSamples()
        {
            var bytesPerSample = GetBytesPerSample();
            var frameCount = SampleCount;
            var perChannelSamples = new float[Channels][];
            for (var channelIndex = 0; channelIndex < Channels; channelIndex++)
                perChannelSamples[channelIndex] = new float[frameCount];

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                for (var channelIndex = 0; channelIndex < Channels; channelIndex++)
                {
                    var byteOffset = (frameIndex * Channels + channelIndex) * bytesPerSample;
                    perChannelSamples[channelIndex][frameIndex] = ReadSample(byteOffset);
                }
            }

            return perChannelSamples;
        }

        private float ReadSample(int byteOffset)
        {
            if (BitsPerSample == 8)
                return (Data[byteOffset] - Unsigned8BitMidpoint) / Unsigned8BitFullScale;

            if (BitsPerSample == 16)
                return BitConverter.ToInt16(Data, byteOffset) / Signed16BitFullScale;

            if (BitsPerSample == 24)
            {
                var sample = Data[byteOffset]
                    | (Data[byteOffset + 1] << BitHelper.BitsPerByte)
                    | (Data[byteOffset + 2] << (BitHelper.BitsPerByte * 2));
                return SignExtend24Bit(sample) / Signed24BitFullScale;
            }

            if (BitsPerSample == 32)
            {
                if (SampleFormat == SampleFormat.Float)
                    return BitConverter.ToSingle(Data, byteOffset);

                return BitConverter.ToInt32(Data, byteOffset) / Signed32BitFullScale;
            }

            throw new InvalidDataException(UnsupportedBitsPerSampleMessage);
        }

        private static int SignExtend24Bit(int sample)
            => (sample & SignBit24Bit) != 0 ? sample | unchecked((int)0xFF000000) : sample;

        private int GetBytesPerSample()
        {
            var bytesPerSample = BitsPerSample / BitHelper.BitsPerByte;
            if (bytesPerSample == 0)
                throw new InvalidDataException(UnsupportedBitsPerSampleMessage);

            return bytesPerSample;
        }

        private string UnsupportedBitsPerSampleMessage
            => $"Unsupported bits per sample: {BitsPerSample}. Supported formats are 8-bit, 16-bit, 24-bit and 32-bit integer, plus 32-bit IEEE float.";
    }
}
