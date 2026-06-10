using System;
using System.IO;
using Editors.BattleMapEditor.ViewModels;
using Shared.Core.DevConfig;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;

namespace Editors.BattleMapEditor.DevConfig
{
    internal class BattleMap_WakaChateau : IDeveloperConfiguration
    {
        private readonly IEditorManager _editorManager;
        private readonly IPackFileContainerLoader _packFileContainerLoader;
        private readonly IPackFileService _packFileService;

        public BattleMap_WakaChateau(
            IEditorManager editorManager,
            IPackFileContainerLoader packFileContainerLoader,
            IPackFileService packFileService)
        {
            _editorManager = editorManager;
            _packFileContainerLoader = packFileContainerLoader;
            _packFileService = packFileService;
        }

        public void OverrideSettings(ApplicationSettings currentSettings)
        {
            currentSettings.CurrentGame = GameTypeEnum.Warhammer3;
            currentSettings.LoadCaPacksByDefault = true;

            var packPath = FindExampleMapPack();
            if (packPath == null)
                throw new FileNotFoundException("Could not find Research/maps/example_map_pack.pack. Make sure the repository root contains the Research/maps directory.");

            var container = _packFileContainerLoader.Load(packPath);
            if (container == null)
                throw new InvalidOperationException($"Failed to load pack file: {packPath}");

            container.IsCaPackFile = true;
            _packFileService.AddContainer(container);
        }

        public void OpenFileOnLoad()
        {
            var editor = _editorManager.Create(EditorEnums.BattleMapViewer_Editor) as BattleMapViewerViewModel;
            editor!.Load("terrain/tiles/battle/domination/waka_chateau");
        }

        private static string? FindExampleMapPack()
        {
            foreach (var startPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(startPath);
                while (directory != null)
                {
                    var candidate = Path.Combine(directory.FullName, "Research", "maps", "example_map_pack.pack");
                    if (File.Exists(candidate))
                        return candidate;
                    directory = directory.Parent;
                }
            }

            return null;
        }
    }
}
