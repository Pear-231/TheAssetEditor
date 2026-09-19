using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc.V136.Shared
{
    public class PositioningParams_V136
    {
        // Bit 0 says the node states its own positioning rather than inheriting its parent's, and
        // bit 1 says three-dimensional positioning is available. Both must be set for the 3D block
        // below to be present, which is the same condition that decides whether a voice is
        // positioned at all — so the reader and the engine share this one definition rather than
        // each spelling the mask out for themselves.
        private const byte OverridesParentPositioningBit = 0x01;
        private const byte Is3DAvailableBit = 0x02;
        private const byte PositionedBits = OverridesParentPositioningBit | Is3DAvailableBit;

        public byte BitsPositioning { get; set; }
        public byte Bits3D { get; set; }
        public byte PathMode { get; set; }
        public float TransitionTime { get; set; }
        public uint NumVertexes { get; set; }
        public List<AkPathVertex_V136> VertexList { get; set; } = [];
        public uint NumPlayListItems { get; set; }
        public List<AkPathListItemOffset_V136> PlayListItems { get; set; } = [];
        public List<Ak3DAutomationParams_V136> Params { get; set; } = [];

        public bool IsPositioned => (BitsPositioning & PositionedBits) == PositionedBits;

        public void ReadData(ByteChunk chunk)
        {
            BitsPositioning = chunk.ReadByte();
            if (IsPositioned)
            {
                Bits3D = chunk.ReadByte();

                var e3DPositionType = BitsPositioning >> 5 & 3;
                var has_automation = e3DPositionType != 0;
                if (has_automation)
                {
                    PathMode = chunk.ReadByte();
                    TransitionTime = chunk.ReadSingle();
                    NumVertexes = chunk.ReadUInt32();
                    for (var i = 0; i < NumVertexes; i++)
                    {
                        var akPathVertex = new AkPathVertex_V136();
                        akPathVertex.ReadData(chunk);
                        VertexList.Add(akPathVertex);
                    }

                    NumPlayListItems = chunk.ReadUInt32();
                    for (var i = 0; i < NumPlayListItems; i++)
                    {
                        var akPathListItemOffset = new AkPathListItemOffset_V136();
                        akPathListItemOffset.ReadData(chunk);
                        PlayListItems.Add(akPathListItemOffset);
                    }

                    for (var i = 0; i < NumPlayListItems; i++)
                    {
                        var ak3DAutomationParams = new Ak3DAutomationParams_V136();
                        ak3DAutomationParams.ReadData(chunk);
                        Params.Add(ak3DAutomationParams);
                    }
                }
            }
        }

        public byte[] WriteData()
        {
            if (BitsPositioning == 0x03 && Bits3D == 0x08)
                return [0x03, 0x08];
            else if (BitsPositioning == 0x00)
                return [0x00];
            else
                throw new NotSupportedException("Users probably don't need this complexity.");
        }

        public uint GetSize()
        {
            if (BitsPositioning == 0x03 && Bits3D == 0x08)
                return 2;
            else if (BitsPositioning == 0x00)
                return 1;
            else
                throw new NotSupportedException("Users probably don't need this complexity.");
        }

        public class AkPathVertex_V136
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

        public class AkPathListItemOffset_V136
        {
            public uint VerticesOffset { get; set; }
            public uint NumVertices { get; set; }

            public void ReadData(ByteChunk chunk)
            {
                VerticesOffset = chunk.ReadUInt32();
                NumVertices = chunk.ReadUInt32();
            }
        }

        public class Ak3DAutomationParams_V136
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
}
