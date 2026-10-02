using System;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.EmbeddedResources;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Shared.Ui.BaseDialogs.PackFileTree.ValueConverters
{
    [ValueConversion(typeof(TreeNode), typeof(BitmapImage))]
    public class PackFileToImageValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is TreeNode node)
            {
                if (node.NodeType == NodeType.Root)
                    return GetRootIcon(node as RootTreeNode);
                else if (node.NodeType == NodeType.Directory)
                    return IconLibrary.FolderIcon;
                if (node.NodeType == NodeType.File)
                {
                    if (IsIgnoredInSystemFolderContainer(node))
                        return IconLibrary.IgnoredFileIcon;

                    return IconLibrary.FileIcon;
                }
            }

            throw new Exception("Unknown type " + value.GetType().FullName);
        }

        private static BitmapImage GetRootIcon(RootTreeNode? root)
        {
            var container = root?.Owner;

            switch(container!.ContainerType)
            {
                case PackFileContainerType.Pack:
                    return IconLibrary.NormalModPackIcon;
                case PackFileContainerType.GamePacks:
                    return IconLibrary.DatabaseModPackIcon;
                case PackFileContainerType.Project:
                    return IconLibrary.SystemFolderModPackIcon;
                default:
                    return IconLibrary.MissingIcon;
            }
        }

        private static bool IsIgnoredInSystemFolderContainer(TreeNode node)
        {
            var container = TreeNodeHelper.GetPackFileContainer(node);
            if (container == null || container.ContainerType != PackFileContainerType.Project)
                return false;

            var normalizedPath = PathNormalization.NormalizeFileName(node.GetFullPath());
            return container.PackFileSettings.IgnoredFilesWhenSerializing
                .Any(x => string.Equals(PathNormalization.NormalizeFileName(x), normalizedPath, StringComparison.OrdinalIgnoreCase));
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
