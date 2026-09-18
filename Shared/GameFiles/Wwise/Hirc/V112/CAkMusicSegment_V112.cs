using System.Text;
using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    public sealed class CAkMusicSegment_V112 : HircItem
    {
        public MusicNodeParams_V112 MusicNodeParams { get; } = new();
        public double Duration { get; private set; }
        public List<MusicMarker_V112> Markers { get; } = [];

        protected override void ReadData(ByteChunk chunk)
        {
            MusicNodeParams.ReadData(chunk);
            Duration = chunk.ReadDouble();
            var markerCount = chunk.ReadUInt32();
            for (var index = 0; index < markerCount; index++)
                Markers.Add(MusicMarker_V112.ReadData(chunk));
        }

        public override byte[] WriteData() => throw new NotSupportedException("Writing music segment HIRCs is not supported.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Writing music segment HIRCs is not supported.");

        public sealed class MusicMarker_V112
        {
            public uint Id { get; private set; }
            public double Position { get; private set; }
            public string Name { get; private set; } = string.Empty;

            public static MusicMarker_V112 ReadData(ByteChunk chunk)
            {
                var marker = new MusicMarker_V112
                {
                    Id = chunk.ReadUInt32(),
                    Position = chunk.ReadDouble()
                };
                var nameLength = chunk.ReadUInt32();
                marker.Name = nameLength == 0 ? string.Empty : Encoding.UTF8.GetString(chunk.ReadBytes(checked((int)nameLength)));
                return marker;
            }
        }
    }
}
