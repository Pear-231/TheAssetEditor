using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    public sealed class CAkAttenuation_V112 : HircItem, ICAkAttenuation
    {
        private const int CurveSlotCount = 7;
        private readonly sbyte[] _curveIndices = new sbyte[CurveSlotCount];
        private readonly List<WwiseCurve> _curves = [];

        public InitialRtpc_V112 InitialRtpc { get; } = new();

        protected override void ReadData(ByteChunk chunk)
        {
            var isConeEnabled = (chunk.ReadByte() & 1) != 0;
            if (isConeEnabled)
                chunk.Advance(5 * sizeof(float));
            for (var curveSlot = 0; curveSlot < CurveSlotCount; curveSlot++)
                _curveIndices[curveSlot] = unchecked((sbyte)chunk.ReadByte());

            var curveCount = chunk.ReadByte();
            for (var curveOrdinal = 0; curveOrdinal < curveCount; curveOrdinal++)
            {
                var scaling = (AkCurveScaling)chunk.ReadByte();
                var pointCount = chunk.ReadUShort();
                var points = new WwiseCurvePoint[pointCount];
                for (var pointOrdinal = 0; pointOrdinal < pointCount; pointOrdinal++)
                    points[pointOrdinal] = new WwiseCurvePoint(chunk.ReadSingle(), chunk.ReadSingle(), chunk.ReadUInt32());
                _curves.Add(new WwiseCurve(scaling, points));
            }
            InitialRtpc.ReadData(chunk);
        }

        public WwiseCurve GetCurve(AttenuationCurveType curveType)
        {
            var curveIndex = _curveIndices[(int)curveType];
            return curveIndex < 0 || curveIndex >= _curves.Count ? WwiseCurve.Empty : _curves[curveIndex];
        }

        public override byte[] WriteData() => throw new NotSupportedException("Writing attenuation HIRCs is not supported.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Writing attenuation HIRCs is not supported.");
    }
}
