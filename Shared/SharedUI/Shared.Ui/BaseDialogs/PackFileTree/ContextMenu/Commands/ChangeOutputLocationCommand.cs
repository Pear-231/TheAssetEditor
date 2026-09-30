using System.IO;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class ChangeOutputLocationCommand(
        IStandardDialogs standardDialogs,
        LocalizationManager localizationManager,
        IScopedLogger scopedLogger) : IContextMenuCommand
    {
        private readonly ILogger _logger = scopedLogger.ForContext<ChangeOutputLocationCommand>();
        private TreeNode _node = null!;

        public string GetDisplayName(TreeNode node) => localizationManager.Get("PackFileTree.ContextMenu.SetPackOutputLocation");

        public bool ShouldAdd(TreeNode node)
        {
            var container = TreeNodeHelper.GetPackFileContainer(node);
            return node.NodeType == NodeType.Root && container is { IsReadOnly: false, ContainerType: PackFileContainerType.SystemFolder };
        }

        public bool IsEnabled(TreeNode node) => true;

        public void Configure(TreeNode node)
        {
            _node = node;
        }

        public void Execute()
        {
            var container = TreeNodeHelper.GetPackFileContainer(_node);
            if (container == null)
            {
                _logger.Here().Warning($"Change output location blocked because no container was resolved for '{CommandLoggingHelper.DescribeNode(_node)}'");
                standardDialogs.ShowDialogBox("Unable to resolve selected packfile", "Error");
                return;
            }

            var initialFileName = string.IsNullOrWhiteSpace(container.PackFileSettings.SaveLocationPath)
                ? container.Name
                : Path.GetFileNameWithoutExtension(container.PackFileSettings.SaveLocationPath);

            var saveDialogResult = standardDialogs.ShowSystemSaveFileDialog(initialFileName, "PackFile | *.pack", "pack");
            if (!saveDialogResult.Result || string.IsNullOrWhiteSpace(saveDialogResult.FilePath))
            {
                _logger.Here().Information($"Change output location cancelled for pack file container '{CommandLoggingHelper.DescribePack(container)}'");
                return;
            }

            var outputPath = Path.ChangeExtension(saveDialogResult.FilePath, ".pack");
            container.PackFileSettings.SaveLocationPath = outputPath;
            container.SaveSettings();
            _logger.Here().Information($"Changed output location for pack file container '{CommandLoggingHelper.DescribePack(container)}' to '{outputPath}'");
        }
    }
}
