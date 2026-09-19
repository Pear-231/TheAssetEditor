using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc.V112.Shared
{
    public class MusicTransNodeParams_V112
    {
        public MusicNodeParams_V112 MusicNodeParams { get; } = new();
        public List<TransitionRule_V112> Rules { get; } = [];

        public void ReadData(ByteChunk chunk)
        {
            MusicNodeParams.ReadData(chunk);
            var ruleCount = chunk.ReadUInt32();
            for (var index = 0; index < ruleCount; index++)
                Rules.Add(TransitionRule_V112.ReadData(chunk));
        }

        public sealed class TransitionRule_V112
        {
            public List<uint> SourceIds { get; } = [];
            public List<uint> DestinationIds { get; } = [];
            public SourceRule_V112 SourceRule { get; private set; } = new();
            public DestinationRule_V112 DestinationRule { get; private set; } = new();
            public byte AllocatesTransitionObject { get; private set; }
            public TransitionObject_V112? TransitionObject { get; private set; }

            public static TransitionRule_V112 ReadData(ByteChunk chunk)
            {
                var result = new TransitionRule_V112();
                var sourceCount = chunk.ReadUInt32();
                for (var index = 0; index < sourceCount; index++) result.SourceIds.Add(chunk.ReadUInt32());
                var destinationCount = chunk.ReadUInt32();
                for (var index = 0; index < destinationCount; index++) result.DestinationIds.Add(chunk.ReadUInt32());
                result.SourceRule = SourceRule_V112.ReadData(chunk);
                result.DestinationRule = DestinationRule_V112.ReadData(chunk);
                result.AllocatesTransitionObject = chunk.ReadByte();
                if (result.AllocatesTransitionObject != 0) result.TransitionObject = TransitionObject_V112.ReadData(chunk);
                return result;
            }
        }

        public sealed class SourceRule_V112
        {
            public int TransitionTime { get; private set; }
            public uint FadeCurve { get; private set; }
            public int FadeOffset { get; private set; }
            public uint SyncType { get; private set; }
            public uint CueFilterHash { get; private set; }
            public byte PlayPostExit { get; private set; }
            public static SourceRule_V112 ReadData(ByteChunk chunk) => new()
            {
                TransitionTime = chunk.ReadInt32(), FadeCurve = chunk.ReadUInt32(), FadeOffset = chunk.ReadInt32(),
                SyncType = chunk.ReadUInt32(), CueFilterHash = chunk.ReadUInt32(), PlayPostExit = chunk.ReadByte()
            };
        }

        public sealed class DestinationRule_V112
        {
            public int TransitionTime { get; private set; }
            public uint FadeCurve { get; private set; }
            public int FadeOffset { get; private set; }
            public uint CueFilterHash { get; private set; }
            public uint JumpToId { get; private set; }
            public ushort EntryType { get; private set; }
            public byte PlayPreEntry { get; private set; }
            public byte MatchSourceCueName { get; private set; }
            public static DestinationRule_V112 ReadData(ByteChunk chunk) => new()
            {
                TransitionTime = chunk.ReadInt32(), FadeCurve = chunk.ReadUInt32(), FadeOffset = chunk.ReadInt32(),
                CueFilterHash = chunk.ReadUInt32(), JumpToId = chunk.ReadUInt32(), EntryType = chunk.ReadUShort(),
                PlayPreEntry = chunk.ReadByte(), MatchSourceCueName = chunk.ReadByte()
            };
        }

        public sealed class TransitionObject_V112
        {
            public uint SegmentId { get; private set; }
            public Fade_V112 FadeIn { get; private set; } = new();
            public Fade_V112 FadeOut { get; private set; } = new();
            public byte PlayPreEntry { get; private set; }
            public byte PlayPostExit { get; private set; }
            public static TransitionObject_V112 ReadData(ByteChunk chunk) => new()
            {
                SegmentId = chunk.ReadUInt32(), FadeIn = Fade_V112.ReadData(chunk), FadeOut = Fade_V112.ReadData(chunk),
                PlayPreEntry = chunk.ReadByte(), PlayPostExit = chunk.ReadByte()
            };
        }

        public sealed class Fade_V112
        {
            public int TransitionTime { get; private set; }
            public uint Curve { get; private set; }
            public int Offset { get; private set; }
            public static Fade_V112 ReadData(ByteChunk chunk) => new() { TransitionTime = chunk.ReadInt32(), Curve = chunk.ReadUInt32(), Offset = chunk.ReadInt32() };
        }
    }
}
