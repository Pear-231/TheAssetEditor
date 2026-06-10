using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering.RenderItems
{
    // Per-instance data layout matching InstancingShader.fx.
    // The shader reconstructs a 4x4 world matrix from the four basis rows:
    //   world[0] = (InstancePosition, 0)  <- matrix row 0 (Right)
    //   world[1] = (InstanceForward,  0)  <- matrix row 1 (Up)
    //   world[2] = (InstanceUp,       0)  <- matrix row 2 (Backward)
    //   world[3] = (InstanceLeft,     1)  <- matrix row 3 (Translation)
    [StructLayout(LayoutKind.Sequential)]
    internal struct MeshInstanceData : IVertexType
    {
        public Vector3 InstancePosition;
        public Vector3 InstanceForward;
        public Vector3 InstanceUp;
        public Vector3 InstanceLeft;
        public Vector3 Colour;

        public static readonly VertexDeclaration VertexDeclaration = new(
            new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position, 1),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 1),
            new VertexElement(24, VertexElementFormat.Vector3, VertexElementUsage.Normal, 2),
            new VertexElement(36, VertexElementFormat.Vector3, VertexElementUsage.Normal, 3),
            new VertexElement(48, VertexElementFormat.Vector3, VertexElementUsage.Normal, 4));

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

        public static MeshInstanceData FromMatrix(Matrix m, Vector3 colour) => new()
        {
            InstancePosition = new Vector3(m.M11, m.M12, m.M13),
            InstanceForward  = new Vector3(m.M21, m.M22, m.M23),
            InstanceUp       = new Vector3(m.M31, m.M32, m.M33),
            InstanceLeft     = new Vector3(m.M41, m.M42, m.M43),
            Colour           = colour,
        };
    }
}
