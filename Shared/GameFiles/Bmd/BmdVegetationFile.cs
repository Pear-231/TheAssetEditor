using System.Collections.Generic;

namespace Shared.GameFormats.Bmd
{
    public class BmdVegetationFile
    {
        public ushort FastBinVersion { get; set; }
        public ushort TreeListVersion { get; set; }
        public ushort GrassListVersion { get; set; }
        public uint GrassGroupCount { get; set; }
        public List<TreeModelGroup> TreeGroups { get; set; } = [];
    }

    public class TreeModelGroup
    {
        public string ModelPath { get; set; } = string.Empty;
        public List<TreeInstance> Instances { get; set; } = [];
    }

    public class TreeInstance
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationDegrees { get; set; }
        public float Scale { get; set; }
        public byte Flags { get; set; }
    }
}
