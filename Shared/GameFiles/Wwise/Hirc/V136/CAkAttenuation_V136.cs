using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    // Bank-generator 136 attenuation ShareSet. Seven property slots select from a compact list of
    // conversion curves; -1 means that property has no distance curve.
    public sealed class CAkAttenuation_V136 : HircItem, ICAkAttenuation
    {
        private const int CurveSlotCount = 7;
        private readonly sbyte[] _curveIndices = new sbyte[CurveSlotCount];
        private readonly List<WwiseCurve> _curves = [];

        public bool IsConeEnabled { get; private set; }
        public InitialRtpc_V136 InitialRtpc { get; } = new();

        protected override void ReadData(ByteChunk chunk)
        {
            IsConeEnabled = (chunk.ReadByte() & 1) != 0;
            if (IsConeEnabled)
            {
                // Inside angle, outside angle, outside volume, LPF and HPF.
                chunk.Advance(5 * sizeof(float));
            }

            for (var curveSlot = 0; curveSlot < CurveSlotCount; curveSlot++)
                _curveIndices[curveSlot] = unchecked((sbyte)chunk.ReadByte());

            var curveCount = chunk.ReadByte();
            for (var curveOrdinal = 0; curveOrdinal < curveCount; curveOrdinal++)
            {
                // The scaling is not decoration: a volume curve stores normalised values that only
                // become decibels once it is applied, so dropping this byte turns full attenuation
                // into a single decibel.
                var scaling = (AkCurveScaling)chunk.ReadByte();
                var pointCount = chunk.ReadUShort();
                var points = new WwiseCurvePoint[pointCount];
                for (var pointOrdinal = 0; pointOrdinal < pointCount; pointOrdinal++)
                {
                    points[pointOrdinal] = new WwiseCurvePoint(
                        chunk.ReadSingle(),
                        chunk.ReadSingle(),
                        chunk.ReadUInt32());
                }
                _curves.Add(new WwiseCurve(scaling, points));
            }

            InitialRtpc.ReadData(chunk);
        }

        public WwiseCurve GetCurve(AttenuationCurveType curveType)
        {
            var slot = (int)curveType;
            if (slot < 0 || slot >= _curveIndices.Length)
                return WwiseCurve.Empty;
            var curveIndex = _curveIndices[slot];
            return curveIndex < 0 || curveIndex >= _curves.Count ? WwiseCurve.Empty : _curves[curveIndex];
        }

        public override byte[] WriteData() => throw new NotSupportedException("Writing attenuation HIRCs is not supported.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Writing attenuation HIRCs is not supported.");
    }
}
