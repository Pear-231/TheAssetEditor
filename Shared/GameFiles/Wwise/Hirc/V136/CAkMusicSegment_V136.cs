using System.Text;
using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    public partial class CAkMusicSegment_V136 : HircItem
    {
        public MusicNodeParams_V136 MusicNodeParams { get; set; } = new MusicNodeParams_V136();
        public double Duration { get; set; }
        public List<AkMusicMarkerWwise_V136> ArrayMarkersList { get; set; } = [];

        protected override void ReadData(ByteChunk chunk, BankVersion bankVersion)
        {
            MusicNodeParams.ReadData(chunk, bankVersion);
            Duration = chunk.ReadInt64();

            var ulNumMarkers = chunk.ReadUInt32();
            for (var i = 0; i < ulNumMarkers; i++)
            {
                var marker = new AkMusicMarkerWwise_V136();
                marker.ReadData(chunk);
                ArrayMarkersList.Add(marker);
            }
        }

        public override byte[] WriteData(BankVersion bankVersion) => throw new NotSupportedException("Users probably don't need this complexity.");
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
                Position = chunk.ReadInt64();
                MarkerName = Encoding.UTF8.GetString(chunk.ReadBytes((int)StringSize));
            }
        }
    }
}
