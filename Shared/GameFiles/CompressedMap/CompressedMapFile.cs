namespace Shared.GameFormats.CompressedMap
{
    public sealed class CompressedMapFile
    {
        public ushort Version { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public int BlockWidth { get; init; }
        public int BlockHeight { get; init; }
        public byte[] UnknownHeaderData { get; init; } = [];
        public float ValueMaximum { get; init; }
        public float ValueMinimum { get; init; }
        public string CodecName { get; init; } = string.Empty;
        public ushort[,] Samples { get; init; } = new ushort[0, 0];

        public float[,] ToFloatSamples()
        {
            var range = ValueMaximum - ValueMinimum;
            var values = new float[Width, Height];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                    values[x, y] = ValueMinimum + (Samples[x, y] / (float)ushort.MaxValue * range);
            }

            return values;
        }
    }
}
