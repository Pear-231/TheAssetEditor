using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering.RenderItems
{
    public sealed class VertexColorTriangleRenderItem : IRenderItem
    {
        private static readonly Dictionary<GraphicsDevice, BasicEffect> Effects = [];

        private readonly VertexPositionColor[] _vertices;
        private readonly Matrix _modelMatrix;

        public VertexColorTriangleRenderItem(VertexPositionColor[] vertices, Matrix modelMatrix)
        {
            _vertices = vertices;
            _modelMatrix = modelMatrix;
        }

        public void Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique)
        {
            if (renderingTechnique != RenderingTechnique.Normal || _vertices.Length < 3)
                return;

            if (!Effects.TryGetValue(device, out var effect))
            {
                effect = new BasicEffect(device)
                {
                    VertexColorEnabled = true,
                    LightingEnabled = false
                };
                Effects.Add(device, effect);
            }

            effect.World = _modelMatrix;
            effect.View = parameters.View;
            effect.Projection = parameters.Projection;

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, _vertices, 0, _vertices.Length / 3);
            }
        }
    }
}
