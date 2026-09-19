using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Wem.V132
{
    public sealed class SmplChunk : RiffChunk
    {
        public const string ChunkTag = "smpl";
        private const int HeaderSize = 9 * sizeof(uint);
        private const int LoopSize = 6 * sizeof(uint);

        public uint LoopStartFrame { get; private set; }
        public uint LoopEndFrame { get; private set; }
        public bool HasForwardLoop { get; private set; }

        public SmplChunk() => Tag = ChunkTag;

        public override void ReadData(ByteChunk chunk)
        {
            if (chunk.BytesLeft < HeaderSize)
                throw new InvalidDataException("WEM smpl chunk is shorter than its header.");

            chunk.Advance(7 * sizeof(uint));
            var loopCount = chunk.ReadUInt32();
            chunk.Advance(sizeof(uint));
            if (loopCount == 0)
                return;
            if (chunk.BytesLeft < LoopSize)
                throw new InvalidDataException("WEM smpl chunk does not contain its declared loop.");

            chunk.Advance(sizeof(uint));
            var loopType = chunk.ReadUInt32();
            var loopStart = chunk.ReadUInt32();
            var inclusiveLoopEnd = chunk.ReadUInt32();
            chunk.Advance(2 * sizeof(uint));
            if (loopType != 0 || inclusiveLoopEnd < loopStart)
                return;

            LoopStartFrame = loopStart;
            LoopEndFrame = checked(inclusiveLoopEnd + 1);
            HasForwardLoop = true;
        }

        public override byte[] WriteData() => throw new NotSupportedException("Writing WEM sampler metadata is not supported.");
    }
}
