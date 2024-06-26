﻿using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.WpfWindow.ResourceHandling;
using Microsoft.Xna.Framework;
using System;

namespace GameWorld.Core.Components
{
    public class GridComponent : BaseComponent, IDisposable
    {
        LineMeshRender _gridMesh;
        private readonly RenderEngineComponent _renderEngineComponent;
        private readonly ResourceLibrary _resourceLibrary;

        public GridComponent(RenderEngineComponent renderEngineComponent, ResourceLibrary resourceLibary)
        {
            _renderEngineComponent = renderEngineComponent;
            _resourceLibrary = resourceLibary;
        }

        public override void Initialize()
        {
            _gridMesh = new LineMeshRender(_resourceLibrary);
            _gridMesh.CreateGrid();

            base.Initialize();
        }

        public override void Draw(GameTime gameTime)
        {
            _renderEngineComponent.AddRenderItem(RenderBuckedId.Line, new LineRenderItem() { LineMesh = _gridMesh, ModelMatrix = Matrix.Identity });
            base.Draw(gameTime);
        }

        public void Dispose()
        {
            if (_gridMesh != null)
                _gridMesh.Dispose();
            _gridMesh = null;
        }
    }
}
