using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.SceneNodes;

namespace Editors.BattleMapEditor.ViewModels
{
    public partial class BattleMapLayerViewModel : ObservableObject
    {
        private readonly GroupNode _sceneNode;

        public string Name { get; }

        [ObservableProperty]
        private bool _isVisible = true;

        public BattleMapLayerViewModel(string name, GroupNode sceneNode)
        {
            Name = name;
            _sceneNode = sceneNode;
        }

        partial void OnIsVisibleChanged(bool value)
            => SetVisibilityRecursive(_sceneNode, value);

        private static void SetVisibilityRecursive(ISceneNode node, bool visible)
        {
            node.IsVisible = visible;
            foreach (var child in node.Children)
                SetVisibilityRecursive(child, visible);
        }
    }
}
