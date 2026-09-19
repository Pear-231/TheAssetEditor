using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc.V112.Shared
{
    public class MusicNodeParams_V112
    {
        public byte Overrides { get; set; }
        public NodeBaseParams_V112 NodeBaseParams { get; } = new();
        public Children_V112 Children { get; } = new();
        public double GridPeriod { get; private set; }
        public double GridOffset { get; private set; }
        public float Tempo { get; private set; }
        public byte TimeSignatureNumerator { get; private set; }
        public byte TimeSignatureDenominator { get; private set; }
        public byte MeterInfoFlag { get; private set; }
        public List<Stinger_V112> Stingers { get; } = [];

        public void ReadData(ByteChunk chunk)
        {
            Overrides = chunk.ReadByte();
            NodeBaseParams.ReadData(chunk);
            Children.ReadData(chunk);
            GridPeriod = chunk.ReadDouble();
            GridOffset = chunk.ReadDouble();
            Tempo = chunk.ReadSingle();
            TimeSignatureNumerator = chunk.ReadByte();
            TimeSignatureDenominator = chunk.ReadByte();
            MeterInfoFlag = chunk.ReadByte();

            var stingerCount = chunk.ReadUInt32();
            for (var index = 0; index < stingerCount; index++)
                Stingers.Add(Stinger_V112.ReadData(chunk));
        }

        public sealed class Stinger_V112
        {
            public uint TriggerId { get; private set; }
            public uint SegmentId { get; private set; }
            public uint SyncPlayAt { get; private set; }
            public uint CueFilterHash { get; private set; }
            public int DontRepeatTime { get; private set; }
            public uint NumSegmentLookAhead { get; private set; }

            public static Stinger_V112 ReadData(ByteChunk chunk) => new()
            {
                TriggerId = chunk.ReadUInt32(),
                SegmentId = chunk.ReadUInt32(),
                SyncPlayAt = chunk.ReadUInt32(),
                CueFilterHash = chunk.ReadUInt32(),
                DontRepeatTime = chunk.ReadInt32(),
                NumSegmentLookAhead = chunk.ReadUInt32()
            };
        }
    }
}
