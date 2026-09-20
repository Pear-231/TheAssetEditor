using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    public class CAkRanSeqCntr_V136 : HircItem, ICAkRanSeqCntr
    {
        public NodeBaseParams_V136 NodeBaseParams { get; set; } = new NodeBaseParams_V136();
        public ushort LoopCount { get; set; }
        public ushort LoopModMin { get; set; }
        public ushort LoopModMax { get; set; }
        public float TransitionTime { get; set; }
        public float TransitionTimeModMin { get; set; }
        public float TransitionTimeModMax { get; set; }
        public ushort AvoidRepeatCount { get; set; }
        public AkTransitionMode TransitionMode { get; set; }
        public AkRandomMode RandomMode { get; set; }
        public AkContainerMode Mode { get; set; }
        public byte BitVector { get; set; }
        public Children_V136 Children { get; set; } = new Children_V136();
        public CAkPlayList_V136 CAkPlayList { get; set; } = new CAkPlayList_V136();

        protected override void ReadData(ByteChunk chunk, BankVersion bankVersion)
        {
            NodeBaseParams.ReadData(chunk, bankVersion);
            LoopCount = chunk.ReadUShort();
            LoopModMin = chunk.ReadUShort();
            LoopModMax = chunk.ReadUShort();
            TransitionTime = chunk.ReadSingle();
            TransitionTimeModMin = chunk.ReadSingle();
            TransitionTimeModMax = chunk.ReadSingle();
            AvoidRepeatCount = chunk.ReadUShort();
            TransitionMode = BankVersion.DecodeTransitionMode(chunk.ReadByte());
            RandomMode = BankVersion.DecodeRandomMode(chunk.ReadByte());
            Mode = BankVersion.DecodeContainerMode(chunk.ReadByte());
            BitVector = chunk.ReadByte();
            Children.ReadData(chunk);
            CAkPlayList.ReadData(chunk);
        }

        public override byte[] WriteData(BankVersion bankVersion)
        {
            using var memStream = WriteHeader();
            memStream.Write(NodeBaseParams.WriteData(bankVersion));
            memStream.Write(ByteParsers.UShort.EncodeValue(LoopCount, out _));
            memStream.Write(ByteParsers.UShort.EncodeValue(LoopModMin, out _));
            memStream.Write(ByteParsers.UShort.EncodeValue(LoopModMax, out _));
            memStream.Write(ByteParsers.Single.EncodeValue(TransitionTime, out _));
            memStream.Write(ByteParsers.Single.EncodeValue(TransitionTimeModMin, out _));
            memStream.Write(ByteParsers.Single.EncodeValue(TransitionTimeModMax, out _));
            memStream.Write(ByteParsers.UShort.EncodeValue(AvoidRepeatCount, out _));
            memStream.Write(ByteParsers.Byte.EncodeValue(BankVersion.EncodeTransitionMode(TransitionMode), out _));
            memStream.Write(ByteParsers.Byte.EncodeValue(BankVersion.EncodeRandomMode(RandomMode), out _));
            memStream.Write(ByteParsers.Byte.EncodeValue(BankVersion.EncodeContainerMode(Mode), out _));
            memStream.Write(ByteParsers.Byte.EncodeValue(BitVector, out _));
            memStream.Write(Children.WriteData());
            memStream.Write(CAkPlayList.WriteData());
            var byteArray = memStream.ToArray();

            // Reload the object to ensure sanity
            var sanityReload = new CAkRanSeqCntr_V136();
            sanityReload.ReadHirc(new ByteChunk(byteArray), bankVersion);

            return byteArray;
        }

        public override void UpdateSectionSize()
        {
            var idSize = ByteHelper.GetPropertyTypeSize(Id);
            var nodeBaseParamsSize = NodeBaseParams.GetSize();
            var loopCountSize = ByteHelper.GetPropertyTypeSize(LoopCount);
            var loopModMinSize = ByteHelper.GetPropertyTypeSize(LoopModMin);
            var loopModMaxSize = ByteHelper.GetPropertyTypeSize(LoopModMax);
            var transitionTimeSize = ByteHelper.GetPropertyTypeSize(TransitionTime);
            var transitionTimeModMinSize = ByteHelper.GetPropertyTypeSize(TransitionTimeModMin);
            var transitionTimeModMaxSize = ByteHelper.GetPropertyTypeSize(TransitionTimeModMax);
            var avoidRepeatCountSize = ByteHelper.GetPropertyTypeSize(AvoidRepeatCount);
            var transitionModeSize = (uint)sizeof(byte);
            var randomModeSize = (uint)sizeof(byte);
            var modeSize = (uint)sizeof(byte);
            var bitVectorSize = ByteHelper.GetPropertyTypeSize(BitVector);
            var childrenSize = Children.GetSize();
            var playListSize = CAkPlayList.GetSize();

            SectionSize = idSize + nodeBaseParamsSize + loopCountSize + loopModMinSize + loopModMaxSize + transitionTimeSize + transitionTimeModMinSize +
                transitionTimeModMaxSize + avoidRepeatCountSize + transitionModeSize + randomModeSize + modeSize + bitVectorSize + childrenSize + playListSize;
        }

        public uint GetDirectParentId() => NodeBaseParams.DirectParentId;
        public List<uint> GetChildren() => CAkPlayList.Playlist.Select(x => x.PlayId).ToList();

        public class CAkPlayList_V136
        {
            public ushort PlayListItem { get; set; }
            public List<AkPlaylistItem_V136> Playlist { get; set; } = [];

            public void ReadData(ByteChunk chunk)
            {
                PlayListItem = chunk.ReadUShort();
                for (var i = 0; i < PlayListItem; i++)
                {
                    var playlistItem = new AkPlaylistItem_V136();
                    playlistItem.ReadData(chunk);
                    Playlist.Add(playlistItem);
                }
            }

            public byte[] WriteData()
            {
                using var memStream = new MemoryStream();
                memStream.Write(ByteParsers.UShort.EncodeValue((ushort)Playlist.Count, out _));
                foreach (var playlistItem in Playlist)
                    memStream.Write(playlistItem.WriteData());
                return memStream.ToArray();
            }

            public uint GetSize()
            {
                var playListItemSize = ByteHelper.GetPropertyTypeSize(PlayListItem);
                uint playListSize = 0;
                foreach (var playlistItem in Playlist)
                    playListSize += playlistItem.GetSize();
                return playListItemSize + playListSize;
            }

            public class AkPlaylistItem_V136
            {
                public uint PlayId { get; set; }
                public int Weight { get; set; }

                public void ReadData(ByteChunk chunk)
                {
                    PlayId = chunk.ReadUInt32();
                    Weight = chunk.ReadInt32();
                }

                public byte[] WriteData()
                {
                    using var memStream = new MemoryStream();
                    memStream.Write(ByteParsers.UInt32.EncodeValue(PlayId, out _));
                    memStream.Write(ByteParsers.Int32.EncodeValue(Weight, out _)); 
                    return memStream.ToArray();
                }

                public uint GetSize()
                {
                    var playIdSize = ByteHelper.GetPropertyTypeSize(PlayId);
                    var weightSize = ByteHelper.GetPropertyTypeSize(Weight);
                    return playIdSize + weightSize;
                }
            }
        }
    }
}

