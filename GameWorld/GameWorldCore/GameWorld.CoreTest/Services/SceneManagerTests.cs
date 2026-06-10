using GameWorld.Core.Components;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Services
{
    internal class SceneManagerTests
    {
        [Test]
        public void ComposeWorldTransform_AppliesChildBeforeParent()
        {
            var child = Matrix.CreateTranslation(2f, 0f, 0f);
            var parent = Matrix.CreateRotationY(MathHelper.PiOver2) * Matrix.CreateTranslation(10f, 0f, 20f);

            var world = SceneManager.ComposeWorldTransform(child, parent);
            var transformedOrigin = Vector3.Transform(Vector3.Zero, world);

            Assert.That(transformedOrigin.X, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(transformedOrigin.Y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(transformedOrigin.Z, Is.EqualTo(18f).Within(0.0001f));
        }
    }
}
