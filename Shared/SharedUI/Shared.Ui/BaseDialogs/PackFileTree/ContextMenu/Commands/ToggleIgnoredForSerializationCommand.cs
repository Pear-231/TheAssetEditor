using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;
using System.Linq;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class ToggleIgnoredForSerializationCommand(LocalizationManager localizationManager, IScopedLogger scopedLogger) : IContextMenuCommand
    {
        private readonly ILogger _logger = scopedLogger.ForContext<ToggleIgnoredForSerializationCommand>();
        private TreeNode _node = null!;

        public string GetDisplayName(TreeNode node)
        {
            var isIgnored = IsIgnored(node);
            return isIgnored
                ? localizationManager.Get("PackFileTree.ContextMenu.RemoveFromIgnoredFiles")
                : localizationManager.Get("PackFileTree.ContextMenu.AddToIgnoredFiles");
        }

        // TODO: Make "Add to Ignored Files" instead "Add to Ignored" and allow it to work on folders and files, 
        // and if it's done for a folder don't display it as an option for the files in that folder.
        public bool ShouldAdd(TreeNode node)
        {
            var container = TreeNodeHelper.GetPackFileContainer(node);
            return node.NodeType == NodeType.File && container?.ContainerType == Shared.Core.PackFiles.Models.PackFileContainerType.SystemFolder;
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
                _logger.Here().Warning("Ignore toggle blocked because no container was resolved for '{Node}'", CommandLoggingHelper.DescribeNode(_node));
                return;
            }

            var normalizedPath = PathNormalization.NormalizeFileName(_node.GetFullPath());
            var ignoredFiles = container.PackFileSettings.IgnoredFilesWhenSerializing;

            var existing = ignoredFiles.FirstOrDefault(x => string.Equals(PathNormalization.NormalizeFileName(x), normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ignoredFiles.Remove(existing);
                _logger.Here().Information("Removed '{Path}' from IgnoredFilesWhenSerializing in '{Pack}'", normalizedPath, CommandLoggingHelper.DescribePack(container));
            }
            else
            {
                ignoredFiles.Add(normalizedPath);
                _logger.Here().Information("Added '{Path}' to IgnoredFilesWhenSerializing in '{Pack}'", normalizedPath, CommandLoggingHelper.DescribePack(container));
            }

            container.SaveSettings();

            // Refresh node visual state so icon converter re-evaluates.
            _node.NotifyNodeVisualChanged();
        }

        private static bool IsIgnored(TreeNode node)
        {
            var container = TreeNodeHelper.GetPackFileContainer(node);
            if (container == null)
                return false;

            var normalizedPath = PathNormalization.NormalizeFileName(node.GetFullPath());
            return container.PackFileSettings.IgnoredFilesWhenSerializing
                .Any(x => string.Equals(PathNormalization.NormalizeFileName(x), normalizedPath, StringComparison.OrdinalIgnoreCase));
        }
    }
}
