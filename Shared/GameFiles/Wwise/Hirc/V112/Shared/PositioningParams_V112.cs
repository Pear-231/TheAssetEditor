using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc.V112.Shared
{
    public class PositioningParams_V112
    {
        // Bit 0 says the node states its own positioning rather than inheriting its parent's, and
        // bit 3 says three-dimensional positioning is available — a different bit from V136's, which
        // uses bit 1. Both must be set for the 3D block below to be present, which is the same
        // condition that decides whether a voice is positioned at all, so the reader and the engine
        // share this one definition rather than each spelling the mask out for themselves.
        private const byte OverridesParentPositioningBit = 0x01;
        private const byte Is3DAvailableBit = 0x08;
        private const byte PositionedBits = OverridesParentPositioningBit | Is3DAvailableBit;

        public byte ByVector { get; set; }
        public byte Bits3D { get; set; }
        public uint AttenuationId { get; set; }
        public byte PathMode { get; set; }
        public float TransitionTime { get; set; }
        public List<AkPathVertex_V112> VertexList { get; set; } = [];
        public List<AkPathListItemOffset_V112> PlayListItems { get; set; } = [];
        public List<Ak3DAutomationParams_V112> Params { get; set; } = [];

        public bool IsPositioned => (ByVector & PositionedBits) == PositionedBits;

        public void ReadData(ByteChunk chunk)
        {
            ByVector = chunk.ReadByte();

            if (IsPositioned)
            {
                Bits3D = chunk.ReadByte();
                AttenuationId = chunk.ReadUInt32();

                if ((Bits3D >> 0 & 1) == 0)
                {
                    PathMode = chunk.ReadByte();
                    TransitionTime = chunk.ReadSingle();

                    var numVertexes = chunk.ReadUInt32();
                    for (var i = 0; i < numVertexes; i++)
                    {
                        var akPathVertex = new AkPathVertex_V112();
                        akPathVertex.ReadData(chunk);
                        VertexList.Add(akPathVertex);
                    }

                    var numPlayListItems = chunk.ReadUInt32();
                    for (var i = 0; i < numPlayListItems; i++)
                    {
                        var akPathListItemOffset = new AkPathListItemOffset_V112();
                        akPathListItemOffset.ReadData(chunk);
                        PlayListItems.Add(akPathListItemOffset);
                    }

                    for (var i = 0; i < numPlayListItems; i++)
                    {
                        var ak3DAutomationParams = new Ak3DAutomationParams_V112();
                        ak3DAutomationParams.ReadData(chunk);
                        Params.Add(ak3DAutomationParams);
                    }
                }
            }
        }

        public byte[] WriteData()
        {
            if (ByVector == 0x03 && Bits3D == 0x08)
                return [0x03, 0x08];
            else if (ByVector == 0x00)
                return [0x00];
            else
                throw new NotSupportedException("Users probably don't need this complexity.");
        }

        public uint GetSize()
        {
            if (ByVector == 0x03 && Bits3D == 0x08)
                return 2;
            else if (ByVector == 0x00)
                return 1;
            else
                throw new NotSupportedException("Users probably don't need this complexity.");
        }
    }

    public class AkPathVertex_V112
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public int Duration { get; set; }

        public void ReadData(ByteChunk chunk)
        {
            X = chunk.ReadSingle();
            Y = chunk.ReadSingle();
            Z = chunk.ReadSingle();
            Duration = chunk.ReadInt32();
        }
    }

    public class AkPathListItemOffset_V112
    {
        public uint VerticesOffset { get; set; }
        public uint NumVertices { get; set; }

        public void ReadData(ByteChunk chunk)
        {
            VerticesOffset = chunk.ReadUInt32();
            NumVertices = chunk.ReadUInt32();
        }
    }

    public class Ak3DAutomationParams_V112
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public void ReadData(ByteChunk chunk)
        {
            X = chunk.ReadSingle();
            Y = chunk.ReadSingle();
            Z = chunk.ReadSingle();
        }
    }
}
