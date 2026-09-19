using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    public sealed class CAkMusicTrack_V112 : HircItem, ICAkMusicTrack
    {
        public byte Overrides { get; private set; }
        public List<AkBankSourceData_V112> Sources { get; } = [];
        public List<TrackSourceInfo_V112> Playlist { get; } = [];
        public uint SubTrackCount { get; private set; }
        public List<ClipAutomation_V112> ClipAutomation { get; } = [];
        public NodeBaseParams_V112 NodeBaseParams { get; } = new();
        public byte TrackType { get; private set; }
        public TrackSwitchParams_V112? SwitchParams { get; private set; }
        public TrackTransitionParams_V112? TransitionParams { get; private set; }
        public int LookAheadTime { get; private set; }

        protected override void ReadData(ByteChunk chunk)
        {
            Overrides = chunk.ReadByte();
            var sourceCount = chunk.ReadUInt32();
            for (var index = 0; index < sourceCount; index++)
                Sources.Add(AkBankSourceData_V112.ReadData(chunk));

            var playlistCount = chunk.ReadUInt32();
            for (var index = 0; index < playlistCount; index++)
                Playlist.Add(TrackSourceInfo_V112.ReadData(chunk));
            if (playlistCount > 0)
                SubTrackCount = chunk.ReadUInt32();

            var automationCount = chunk.ReadUInt32();
            for (var index = 0; index < automationCount; index++)
                ClipAutomation.Add(ClipAutomation_V112.ReadData(chunk));

            NodeBaseParams.ReadData(chunk);
            TrackType = chunk.ReadByte();
            if (TrackType == 3)
            {
                SwitchParams = TrackSwitchParams_V112.ReadData(chunk);
                TransitionParams = TrackTransitionParams_V112.ReadData(chunk);
            }
            LookAheadTime = chunk.ReadInt32();
        }

        public override byte[] WriteData() => throw new NotSupportedException("Writing music track HIRCs is not supported.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Writing music track HIRCs is not supported.");
        public List<uint> GetChildren() => Sources.Select(source => source.AkMediaInformation.SourceId).ToList();

        public sealed class TrackSourceInfo_V112
        {
            public uint TrackId { get; private set; }
            public uint SourceId { get; private set; }
            public double PlayAt { get; private set; }
            public double BeginTrimOffset { get; private set; }
            public double EndTrimOffset { get; private set; }
            public double SourceDuration { get; private set; }

            public static TrackSourceInfo_V112 ReadData(ByteChunk chunk) => new()
            {
                TrackId = chunk.ReadUInt32(), SourceId = chunk.ReadUInt32(), PlayAt = chunk.ReadDouble(),
                BeginTrimOffset = chunk.ReadDouble(), EndTrimOffset = chunk.ReadDouble(), SourceDuration = chunk.ReadDouble()
            };
        }

        public sealed class ClipAutomation_V112
        {
            public uint ClipIndex { get; private set; }
            public uint AutomationType { get; private set; }
            public List<AkRtpcGraphPoint_V112> Points { get; } = [];

            public static ClipAutomation_V112 ReadData(ByteChunk chunk)
            {
                var result = new ClipAutomation_V112 { ClipIndex = chunk.ReadUInt32(), AutomationType = chunk.ReadUInt32() };
                var count = chunk.ReadUInt32();
                for (var index = 0; index < count; index++)
                    result.Points.Add(AkRtpcGraphPoint_V112.ReadData(chunk));
                return result;
            }
        }

        public sealed class TrackSwitchParams_V112
        {
            public byte GroupType { get; private set; }
            public uint GroupId { get; private set; }
            public uint DefaultSwitch { get; private set; }
            public List<uint> Associations { get; } = [];

            public static TrackSwitchParams_V112 ReadData(ByteChunk chunk)
            {
                var result = new TrackSwitchParams_V112 { GroupType = chunk.ReadByte(), GroupId = chunk.ReadUInt32(), DefaultSwitch = chunk.ReadUInt32() };
                var count = chunk.ReadUInt32();
                for (var index = 0; index < count; index++) result.Associations.Add(chunk.ReadUInt32());
                return result;
            }
        }

        public sealed class TrackTransitionParams_V112
        {
            public MusicFade_V112 SourceFade { get; private set; } = new();
            public uint SyncType { get; private set; }
            public uint CueFilterHash { get; private set; }
            public MusicFade_V112 DestinationFade { get; private set; } = new();

            public static TrackTransitionParams_V112 ReadData(ByteChunk chunk) => new()
            {
                SourceFade = MusicFade_V112.ReadData(chunk), SyncType = chunk.ReadUInt32(), CueFilterHash = chunk.ReadUInt32(), DestinationFade = MusicFade_V112.ReadData(chunk)
            };
        }

        public sealed class MusicFade_V112
        {
            public int TransitionTime { get; private set; }
            public uint Curve { get; private set; }
            public int Offset { get; private set; }
            public static MusicFade_V112 ReadData(ByteChunk chunk) => new() { TransitionTime = chunk.ReadInt32(), Curve = chunk.ReadUInt32(), Offset = chunk.ReadInt32() };
        }
    }
}
