using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.SceneNodes;
using Serilog;
using Shared.Core.Services;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;
using Editors.BattleMapEditor.Services;
using Editors.BmdEditor.Services;

namespace Editors.BattleMapEditor.ViewModels
{
    public partial class BattleMapViewerViewModel : ObservableObject, IEditorInterface
    {
        private readonly ILogger _logger = Log.ForContext<BattleMapViewerViewModel>();
        private readonly BmdSceneCreator _sceneCreator;
        private readonly BattleMapFolderLoader _folderLoader;
        private readonly RenderEngineComponent _renderEngine;

        [ObservableProperty] private string _displayName = "Battle Map Viewer";
        [ObservableProperty] private string _mapFolderPath = string.Empty;
        [ObservableProperty] private string _statusText = "No map loaded. Enter a tile folder path and click Load.";
        [ObservableProperty] private bool _useInstancing = true;

        partial void OnUseInstancingChanged(bool value) => _renderEngine.UseInstancing = value;

        public IWpfGame Scene { get; }
        public ObservableCollection<BattleMapLayerViewModel> Layers { get; } = [];
        public ObservableCollection<BattleMapResourceViewModel> Resources { get; } = [];

        public IRelayCommand LoadCommand { get; }

        public BattleMapViewerViewModel(
            IWpfGame gameWorld,
            IComponentInserter componentInserter,
            RenderEngineComponent renderEngine,
            BmdSceneCreator sceneCreator,
            BattleMapFolderLoader folderLoader)
        {
            Scene = gameWorld;
            componentInserter.Execute();
            _renderEngine = renderEngine;
            _sceneCreator = sceneCreator;
            _folderLoader = folderLoader;
            LoadCommand = new RelayCommand(ExecuteLoad, CanLoad);
        }

        partial void OnMapFolderPathChanged(string value)
            => LoadCommand.NotifyCanExecuteChanged();

        private bool CanLoad() => !string.IsNullOrWhiteSpace(MapFolderPath);

        public void Load(string folderPath)
        {
            MapFolderPath = folderPath;
            ExecuteLoad();
        }

        public void Close() { }

        private void ExecuteLoad()
        {
            if (string.IsNullOrWhiteSpace(MapFolderPath))
                return;

            try
            {
                StatusText = "Loading...";
                Layers.Clear();
                Resources.Clear();

                var result = _folderLoader.Load(MapFolderPath);
                var sceneRoot = _sceneCreator.CreateBattleMapRoot(MapFolderPath);

                // Terrain from primary BMD as its own toggleable layer
                if (result.PrimaryPackFile != null)
                {
                    var terrainGroup = sceneRoot.AddObject(new GroupNode("Terrain") { IsEditable = false });
                    _sceneCreator.LoadTerrainIntoGroup(result.PrimaryPackFile, terrainGroup);
                    Layers.Add(new BattleMapLayerViewModel("Terrain", terrainGroup));
                }

                // Each BMD (primary + layer BMDs) as its own layer
                foreach (var layer in result.Layers)
                {
                    var layerGroup = sceneRoot.AddObject(new GroupNode(layer.Name) { IsEditable = false });
                    var propsGroup = layerGroup.AddObject(new GroupNode("Props") { IsEditable = false });
                    var otherGroup = layerGroup.AddObject(new GroupNode("Other") { IsEditable = false });
                    _sceneCreator.LoadBmdContentNoTracking(layer.BmdFile, layer.PackFile, propsGroup, otherGroup);
                    Layers.Add(new BattleMapLayerViewModel(layer.Name, layerGroup));
                }

                // Populate resource list
                foreach (var resource in result.Resources)
                    Resources.Add(new BattleMapResourceViewModel(resource.FileName, resource.PackPath, resource.Type, resource.IsFound));

                _sceneCreator.CompleteLoadLog();

                var layerCount = result.Layers.Count;
                var foundCount = result.Resources.Count(r => r.IsFound);
                StatusText = $"Loaded {layerCount} layer(s), {foundCount}/{result.Resources.Count} resources found";
                DisplayName = $"Battle Map Viewer - {System.IO.Path.GetFileName(MapFolderPath.TrimEnd('/', '\\'))}";
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load battle map from {Path}: {Message}", MapFolderPath, ex.Message);
                StatusText = $"Error: {ex.Message}";
            }
        }
    }
}
