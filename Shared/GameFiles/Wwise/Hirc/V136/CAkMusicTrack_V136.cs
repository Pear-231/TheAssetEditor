using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;
using Shared.GameFormats.Wwise.Versions;

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
        public int LookAheadTime { get; set; }

        protected override void ReadData(ByteChunk chunk, BankVersion bankVersion)
        {
            Flags = chunk.ReadByte();
            NumSources = chunk.ReadUInt32();
            for (var i = 0; i < NumSources; i++)
            {
                var source = new AkBankSourceData_V136();
                source.ReadData(chunk, bankVersion);
                SourceList.Add(source);
            }

            NumPlaylistItem = chunk.ReadUInt32();
            for (var i = 0; i < NumPlaylistItem; i++)
            {
                var trackSrcInfo = new AkTrackSrcInfo_V136();
                trackSrcInfo.ReadData(chunk);
                PlaylistList.Add(trackSrcInfo);
            }

            if (NumPlaylistItem > 0)
                NumSubTrack = chunk.ReadUInt32();

            var numClipAutomationItem = chunk.ReadUInt32();
            for (var i = 0; i < numClipAutomationItem; i++)
            {
                var clipAutomation = new AkClipAutomation_V136();
                clipAutomation.ReadData(chunk);
                ItemsList.Add(clipAutomation);
            }

            NodeBaseParams.ReadData(chunk, bankVersion);
            TrackType = chunk.ReadByte();
            LookAheadTime = chunk.ReadInt32();
        }

        public override void UpdateSectionSize() => throw new NotSupportedException("Users probably don't need this complexity.");
        public override byte[] WriteData(BankVersion bankVersion) => throw new NotSupportedException("Users probably don't need this complexity.");

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

            public void ReadData(ByteChunk chunk)
            {
                TrackId = chunk.ReadUInt32();
                SourceId = chunk.ReadUInt32();
                EventId = chunk.ReadUInt32();
                PlayAt = chunk.ReadInt64();
                BeginTrimOffset = chunk.ReadInt64();
                EndTrimOffset = chunk.ReadInt64();
                SrcDuration = chunk.ReadInt64();
            }
        }

        public class AkClipAutomation_V136
        {
            public uint ClipIndex { get; set; }
            public uint AutoType { get; set; }
            public List<AkRtpcGraphPoint_V136> RtpcMgr { get; set; } = [];

            public void ReadData(ByteChunk chunk)
            {
                ClipIndex = chunk.ReadUInt32();
                AutoType = chunk.ReadUInt32();
                var uNumPoints = chunk.ReadUInt32();
                for (var i = 0; i < uNumPoints; i++)
                {
                    var rtpcGraphPoint = new AkRtpcGraphPoint_V136();
                    rtpcGraphPoint.ReadData(chunk);
                    RtpcMgr.Add(rtpcGraphPoint);
                }
            }
        }
    }
}
