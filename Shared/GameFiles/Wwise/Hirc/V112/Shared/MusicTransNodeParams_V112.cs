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
            {
                var transitionRule = new TransitionRule_V112();
                transitionRule.ReadData(chunk);
                Rules.Add(transitionRule);
            }
        }

        public sealed class TransitionRule_V112
        {
            public List<uint> SourceIds { get; } = [];
            public List<uint> DestinationIds { get; } = [];
            public SourceRule_V112 SourceRule { get; private set; } = new();
            public DestinationRule_V112 DestinationRule { get; private set; } = new();
            public byte AllocatesTransitionObject { get; private set; }
            public TransitionObject_V112? TransitionObject { get; private set; }

            public void ReadData(ByteChunk chunk)
            {
                var sourceCount = chunk.ReadUInt32();
                for (var index = 0; index < sourceCount; index++)
                    SourceIds.Add(chunk.ReadUInt32());

                var destinationCount = chunk.ReadUInt32();
                for (var index = 0; index < destinationCount; index++)
                    DestinationIds.Add(chunk.ReadUInt32());

                SourceRule.ReadData(chunk);
                DestinationRule.ReadData(chunk);
                AllocatesTransitionObject = chunk.ReadByte();
                if (AllocatesTransitionObject != 0)
                {
                    TransitionObject = new TransitionObject_V112();
                    TransitionObject.ReadData(chunk);
                }
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
            public void ReadData(ByteChunk chunk)
            {
                TransitionTime = chunk.ReadInt32();
                FadeCurve = chunk.ReadUInt32();
                FadeOffset = chunk.ReadInt32();
                SyncType = chunk.ReadUInt32();
                CueFilterHash = chunk.ReadUInt32();
                PlayPostExit = chunk.ReadByte();
            }
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
            public void ReadData(ByteChunk chunk)
            {
                TransitionTime = chunk.ReadInt32();
                FadeCurve = chunk.ReadUInt32();
                FadeOffset = chunk.ReadInt32();
                CueFilterHash = chunk.ReadUInt32();
                JumpToId = chunk.ReadUInt32();
                EntryType = chunk.ReadUShort();
                PlayPreEntry = chunk.ReadByte();
                MatchSourceCueName = chunk.ReadByte();
            }
        }

        public sealed class TransitionObject_V112
        {
            public uint SegmentId { get; private set; }
            public Fade_V112 FadeIn { get; private set; } = new();
            public Fade_V112 FadeOut { get; private set; } = new();
            public byte PlayPreEntry { get; private set; }
            public byte PlayPostExit { get; private set; }
            public void ReadData(ByteChunk chunk)
            {
                SegmentId = chunk.ReadUInt32();
                FadeIn.ReadData(chunk);
                FadeOut.ReadData(chunk);
                PlayPreEntry = chunk.ReadByte();
                PlayPostExit = chunk.ReadByte();
            }
        }

        public sealed class Fade_V112
        {
            public int TransitionTime { get; private set; }
            public uint Curve { get; private set; }
            public int Offset { get; private set; }
            public void ReadData(ByteChunk chunk)
            {
                TransitionTime = chunk.ReadInt32();
                Curve = chunk.ReadUInt32();
                Offset = chunk.ReadInt32();
            }
        }
    }
}
