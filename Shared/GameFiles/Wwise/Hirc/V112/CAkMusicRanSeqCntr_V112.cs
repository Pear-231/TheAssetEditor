using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    public sealed class CAkMusicRanSeqCntr_V112 : HircItem
    {
        public MusicTransNodeParams_V112 MusicTransNodeParams { get; } = new();
        public uint PlaylistItemCount { get; private set; }
        public PlaylistItem_V112? Playlist { get; private set; }

        protected override void ReadData(ByteChunk chunk)
        {
            MusicTransNodeParams.ReadData(chunk);
            PlaylistItemCount = chunk.ReadUInt32();
            Playlist = new PlaylistItem_V112();
            Playlist.ReadData(chunk);
        }

        public override byte[] WriteData() => throw new NotSupportedException("Writing music random-sequence HIRCs is not supported.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Writing music random-sequence HIRCs is not supported.");

        public sealed class PlaylistItem_V112
        {
            public uint SegmentId { get; private set; }
            public int PlaylistItemId { get; private set; }
            public uint ChildCount { get; private set; }
            public uint SequenceType { get; private set; }
            public short Loop { get; private set; }
            public short LoopMinimum { get; private set; }
            public short LoopMaximum { get; private set; }
            public uint Weight { get; private set; }
            public ushort AvoidRepeatCount { get; private set; }
            public byte IsUsingWeight { get; private set; }
            public byte IsShuffle { get; private set; }
            public List<PlaylistItem_V112> Children { get; } = [];

            public void ReadData(ByteChunk chunk)
            {
                SegmentId = chunk.ReadUInt32();
                PlaylistItemId = chunk.ReadInt32();
                ChildCount = chunk.ReadUInt32();
                SequenceType = chunk.ReadUInt32();
                Loop = chunk.ReadShort();
                LoopMinimum = chunk.ReadShort();
                LoopMaximum = chunk.ReadShort();
                Weight = chunk.ReadUInt32();
                AvoidRepeatCount = chunk.ReadUShort();
                IsUsingWeight = chunk.ReadByte();
                IsShuffle = chunk.ReadByte();

                for (var index = 0; index < ChildCount; index++)
                {
                    var playlistItem = new PlaylistItem_V112();
                    playlistItem.ReadData(chunk);
                    Children.Add(playlistItem);
                }
            }
        }
    }
}
