using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Envs
{
    // The ENVS chunk: the obstruction and occlusion curves the environment manager applies.
    //
    // Two curve types by three targets -- volume, low-pass and high-pass -- for every bank version
    // between 90 and 150, which covers both versions this repo reads. Nothing consumes these yet;
    // they are read because the init bank cannot be opened at all until every chunk in it can be.
    public class EnvsChunk
    {
        // Obstruction and occlusion, each against volume, low-pass filter and high-pass filter.
        private const int CurveTypeCount = 2;
        private const int CurveTargetCount = 3;

        public ChunkHeader ChunkHeader { get; set; } = new ChunkHeader();
        public List<ObstructionOcclusionCurve> Curves { get; set; } = [];

        public static EnvsChunk ReadData(string fileName, ByteChunk chunk)
        {
            var envsChunk = new EnvsChunk { ChunkHeader = ChunkHeader.ReadData(chunk) };
            for (var curveType = 0; curveType < CurveTypeCount; curveType++)
            {
                for (var curveTarget = 0; curveTarget < CurveTargetCount; curveTarget++)
                {
                    var curve = new ObstructionOcclusionCurve();
                    curve.ReadData(chunk);
                    envsChunk.Curves.Add(curve);
                }
            }
            return envsChunk;
        }

        public class ObstructionOcclusionCurve
        {
            public byte IsCurveEnabled { get; set; }
            public AkCurveScaling Scaling { get; set; }
            public ushort PointCount { get; set; }
            public List<WwiseCurvePoint> Points { get; set; } = [];

            public void ReadData(ByteChunk chunk)
            {
                IsCurveEnabled = chunk.ReadByte();
                Scaling = (AkCurveScaling)chunk.ReadByte();
                PointCount = chunk.ReadUShort();
                for (var pointIndex = 0; pointIndex < PointCount; pointIndex++)
                    Points.Add(new WwiseCurvePoint(chunk.ReadSingle(), chunk.ReadSingle(), chunk.ReadUInt32()));
            }
        }
    }
}
