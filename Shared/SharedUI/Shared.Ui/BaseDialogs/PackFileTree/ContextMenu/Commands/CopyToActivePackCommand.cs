using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class CopyToActivePackCommand(IPackFileService packFileService, IStandardDialogs standardDialogs, LocalizationManager localizationManager, IScopedLogger scopedLogger) : IContextMenuCommand
    {
        private readonly ILogger _logger = scopedLogger.ForContext<CopyToActivePackCommand>();

        public string GetDisplayName(TreeNode node) => localizationManager.Get("PackFileTree.ContextMenu.CopyToActivePack");

        public bool ShouldAdd(TreeNode node)
        {
            var container = TreeNodeHelper.GetPackFileContainer(node);
            var activePack = packFileService.GetActivePack();
            return activePack != null && container != null && activePack != container && node.NodeType != NodeType.Root;
        }

        public bool IsEnabled(TreeNode node) => true;

        private TreeNode _node = null!;

        public void Configure(TreeNode node)
        {
            _node = node;
        }

        public void Execute()
        {
            var activePack = packFileService.GetActivePack();
            if (activePack == null)
            {
                _logger.Here().Warning($"Copy to active pack requested for '{CommandLoggingHelper.DescribeNode(_node)}' but no active pack is selected");
                standardDialogs.ShowDialogBox("No active pack selected!");
                return;
            }

            var container = TreeNodeHelper.GetPackFileContainer(_node);
            if (container == null)
            {
                _logger.Here().Warning($"Copy to active pack blocked because no container was resolved for '{CommandLoggingHelper.DescribeNode(_node)}'");
                standardDialogs.ShowDialogBox("Unable to resolve selected packfile");
                return;
            }

            using (standardDialogs.ShowWaitCursor())
            {
                var files = _node.GetAllChildFileNodes();
                _logger.Here().Information($"Copying {files.Count} file(s) from '{CommandLoggingHelper.DescribeNode(_node)}' to active pack '{CommandLoggingHelper.DescribePack(activePack)}'");
                foreach (var file in files)
                    packFileService.CopyFileFromOtherPackFile(container, file.GetFullPath(), activePack);

                _logger.Here().Information($"Copied {files.Count} file(s) from '{CommandLoggingHelper.DescribeNode(_node)}' to active pack '{CommandLoggingHelper.DescribePack(activePack)}'");
            }
        }
    }
}
