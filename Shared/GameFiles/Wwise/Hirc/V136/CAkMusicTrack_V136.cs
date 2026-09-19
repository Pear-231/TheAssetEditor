using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    public class CAkMusicTrack_V136 : HircItem, ICAkMusicTrack
    {
        public byte Flags { get; set; }
        public uint NumSources { get; set; }
        public List<AkBankSourceData_V136> SourceList { get; set; } = [];
        public uint NumPlaylistItem { get; set; }
        public List<AkTrackSrcInfo_V136> PlaylistList { get; set; } = [];
        public uint NumSubTrack { get; set; }
        public List<AkClipAutomation_V136> ItemsList { get; set; } = [];
        public NodeBaseParams_V136 NodeBaseParams { get; set; } = new NodeBaseParams_V136();
        public byte TrackType { get; set; }
        public TrackSwitchParams_V136? SwitchParams { get; set; }
        public TrackTransitionParams_V136? TransitionParams { get; set; }
        public int LookAheadTime { get; set; }

        protected override void ReadData(ByteChunk chunk)
        {
            Flags = chunk.ReadByte();
            NumSources = chunk.ReadUInt32();
            for (var i = 0; i < NumSources; i++)
                SourceList.Add(AkBankSourceData_V136.ReadData(chunk));

            NumPlaylistItem = chunk.ReadUInt32();
            for (var i = 0; i < NumPlaylistItem; i++)
                PlaylistList.Add(AkTrackSrcInfo_V136.ReadData(chunk));

            if (NumPlaylistItem > 0)
                NumSubTrack = chunk.ReadUInt32();

            var numClipAutomationItem = chunk.ReadUInt32();
            for (var i = 0; i < numClipAutomationItem; i++)
                ItemsList.Add(AkClipAutomation_V136.ReadData(chunk));

            NodeBaseParams.ReadData(chunk);
            TrackType = chunk.ReadByte();
            if (TrackType == 3)
            {
                SwitchParams = TrackSwitchParams_V136.ReadData(chunk);
                TransitionParams = TrackTransitionParams_V136.ReadData(chunk);
            }
            LookAheadTime = chunk.ReadInt32();
        }

        public override void UpdateSectionSize() => throw new NotSupportedException("Users probably don't need this complexity.");
        public override byte[] WriteData() => throw new NotSupportedException("Users probably don't need this complexity.");

        public List<uint> GetChildren() => SourceList.Select(x => x.AkMediaInformation.SourceId).ToList();

        public class AkTrackSrcInfo_V136
        {
            public uint TrackId { get; set; }
            public uint SourceId { get; set; }
            public uint EventId { get; set; }
            public double PlayAt { get; set; }
            public double BeginTrimOffset { get; set; }
            public double EndTrimOffset { get; set; }
            public double SrcDuration { get; set; }

            public static AkTrackSrcInfo_V136 ReadData(ByteChunk chunk)
            {
                var akTrackSrcInfo = new AkTrackSrcInfo_V136()
                {
                    TrackId = chunk.ReadUInt32(),
                    SourceId = chunk.ReadUInt32(),
                    EventId = chunk.ReadUInt32(),
                    PlayAt = chunk.ReadDouble(),
                    BeginTrimOffset = chunk.ReadDouble(),
                    EndTrimOffset = chunk.ReadDouble(),
                    SrcDuration = chunk.ReadDouble(),
                };
                return akTrackSrcInfo;
            }
        }

        public class AkClipAutomation_V136
        {
            public uint ClipIndex { get; set; }
            public uint AutoType { get; set; }
            public List<AkRtpcGraphPoint_V136> RtpcMgr { get; set; } = [];

            public static AkClipAutomation_V136 ReadData(ByteChunk chunk)
            {
                var akClipAutomation = new AkClipAutomation_V136();
                akClipAutomation.ClipIndex = chunk.ReadUInt32();
                akClipAutomation.AutoType = chunk.ReadUInt32();
                var uNumPoints = chunk.ReadUInt32();
                for (var i = 0; i < uNumPoints; i++)
                    akClipAutomation.RtpcMgr.Add(AkRtpcGraphPoint_V136.ReadData(chunk));
                return akClipAutomation;
            }
        }

        public class TrackSwitchParams_V136
        {
            public byte GroupType { get; set; }
            public uint GroupId { get; set; }
            public uint DefaultSwitch { get; set; }
            public List<uint> SwitchAssociations { get; set; } = [];

            public static TrackSwitchParams_V136 ReadData(ByteChunk chunk)
            {
                var instance = new TrackSwitchParams_V136
                {
                    GroupType = chunk.ReadByte(),
                    GroupId = chunk.ReadUInt32(),
                    DefaultSwitch = chunk.ReadUInt32()
                };
                var count = chunk.ReadUInt32();
                for (var index = 0; index < count; index++)
                    instance.SwitchAssociations.Add(chunk.ReadUInt32());
                return instance;
            }
        }

        public class TrackTransitionParams_V136
        {
            public MusicFade_V136 SourceFade { get; set; } = new MusicFade_V136();
            public uint SyncType { get; set; }
            public uint CueFilterHash { get; set; }
            public MusicFade_V136 DestinationFade { get; set; } = new MusicFade_V136();

            public static TrackTransitionParams_V136 ReadData(ByteChunk chunk)
                => new TrackTransitionParams_V136
                {
                    SourceFade = MusicFade_V136.ReadData(chunk),
                    SyncType = chunk.ReadUInt32(),
                    CueFilterHash = chunk.ReadUInt32(),
                    DestinationFade = MusicFade_V136.ReadData(chunk)
                };
        }

        public class MusicFade_V136
        {
            public int TransitionTime { get; set; }
            public uint Curve { get; set; }
            public int FadeOffset { get; set; }

            public static MusicFade_V136 ReadData(ByteChunk chunk)
                => new MusicFade_V136
                {
                    TransitionTime = chunk.ReadInt32(),
                    Curve = chunk.ReadUInt32(),
                    FadeOffset = chunk.ReadInt32()
                };
        }
    }
}
