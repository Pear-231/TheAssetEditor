using System.Text;
using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    public partial class CAkMusicSegment_V136 : HircItem
    {
        public MusicNodeParams_V136 MusicNodeParams { get; set; } = new MusicNodeParams_V136();
        public double Duration { get; set; }
        public List<AkMusicMarkerWwise_V136> ArrayMarkersList { get; set; } = [];

        protected override void ReadData(ByteChunk chunk)
        {
            MusicNodeParams.ReadData(chunk);
            Duration = chunk.ReadDouble();

            var ulNumMarkers = chunk.ReadUInt32();
            for (var i = 0; i < ulNumMarkers; i++)
            {
                var akMusicMarkerWwise = new AkMusicMarkerWwise_V136();
                akMusicMarkerWwise.ReadData(chunk);
                ArrayMarkersList.Add(akMusicMarkerWwise);
            }
        }

        public override byte[] WriteData() => throw new NotSupportedException("Users probably don't need this complexity.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Users probably don't need this complexity.");

        public class AkMusicMarkerWwise_V136
        {
            public uint Id { get; set; }
            public double Position { get; set; }
            public uint StringSize { get; set; }
            public string? MarkerName { get; set; }

            public void ReadData(ByteChunk chunk)
            {
                Id = chunk.ReadUInt32();
                Position = chunk.ReadDouble();
                StringSize = chunk.ReadUInt32();
                MarkerName = Encoding.UTF8.GetString(chunk.ReadBytes((int)StringSize));
            }
        }
    }
}
