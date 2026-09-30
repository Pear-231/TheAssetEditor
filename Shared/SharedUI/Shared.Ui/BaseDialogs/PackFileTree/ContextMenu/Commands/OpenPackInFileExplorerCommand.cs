using System.Diagnostics;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class OpenPackInFileExplorerCommand(IPackFileService packFileService, IStandardDialogs standardDialogs, IFileSystemAccess fileSystemAccess, LocalizationManager localizationManager, IScopedLogger scopedLogger) : IContextMenuCommand
    {
        private readonly ILogger _logger = scopedLogger.ForContext<OpenPackInFileExplorerCommand>();

        public string GetDisplayName(TreeNode node) => localizationManager.Get("PackFileTree.ContextMenu.OpenInFileExplorer");

        public bool ShouldAdd(TreeNode node)
        {
            var container = TreeNodeHelper.GetPackFileContainer(node);
            if (container is not { IsReadOnly: false })
                return false;

            if (node.NodeType != NodeType.File)
                return true;

            return container.ContainerType == PackFileContainerType.SystemFolder;
        }

        public bool IsEnabled(TreeNode node) => true;

        private TreeNode _node = null!;

        public void Configure(TreeNode node)
        {
            _node = node;
        }

        public void Execute()
        {
            var container = TreeNodeHelper.GetPackFileContainer(_node);
            if (container == null)
            {
                _logger.Here().Warning($"Open in File Explorer blocked because no container was resolved for '{CommandLoggingHelper.DescribeNode(_node)}'");
                standardDialogs.ShowDialogBox("Unable to resolve selected packfile");
                return;
            }

            var systemFilePath = container.SystemFilePath;
            if (string.IsNullOrEmpty(systemFilePath))
            {
                _logger.Here().Warning($"Open in File Explorer blocked because pack '{CommandLoggingHelper.DescribePack(container)}' has not been saved to disk");
                standardDialogs.ShowDialogBox("Pack file has not been saved to disk yet.");
                return;
            }

            if (!fileSystemAccess.DirectoryExists(systemFilePath))
                systemFilePath = fileSystemAccess.PathGetDirectoryName(systemFilePath);

            if (systemFilePath == null)
            {
                _logger.Here().Warning($"Unable to determine folder for pack '{CommandLoggingHelper.DescribePack(container)}'");
                standardDialogs.ShowDialogBox("Unable to determine folder for pack file.");
                return;
            }

            using var process = fileSystemAccess.ProcessStart(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{systemFilePath}\"",
                UseShellExecute = true
            });

            if (process == null)
                _logger.Here().Warning($"Explorer launch returned null for folder '{systemFilePath}'");
            else
                _logger.Here().Information($"Opened File Explorer for '{systemFilePath}'");
        }
    }
}
