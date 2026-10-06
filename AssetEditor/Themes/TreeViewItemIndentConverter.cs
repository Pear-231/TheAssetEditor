using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace AssetEditor.Themes
{
    /// <summary>
    /// Converts a tree view item into the horizontal indent for its nesting depth, so each row can be drawn across the full
    /// width of the tree (for a full-row highlight) while its content is still indented.
    /// </summary>
    public class TreeViewItemIndentConverter : IValueConverter
    {
        private const double IndentPerLevel = 12;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var depth = 0;
            var parent = value is TreeViewItem item ? ItemsControl.ItemsControlFromItemContainer(item) : null;
            while (parent is TreeViewItem parentItem)
            {
                depth++;
                parent = ItemsControl.ItemsControlFromItemContainer(parentItem);
            }

            return depth * IndentPerLevel;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
