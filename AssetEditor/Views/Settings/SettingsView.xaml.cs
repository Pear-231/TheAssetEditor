using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AssetEditor.ViewModels;

namespace AssetEditor.Views.Settings
{
    public partial class SettingsView : UserControl
    {
        private sealed class StickyHeaderGroup
        {
            public required TextBlock Header { get; init; }
            public required FrameworkElement Panel { get; init; }
            public double NaturalTop { get; set; }
        }

        private StickyHeaderGroup[] _visibleGroups = [];

        public SettingsView()
        {
            InitializeComponent();
            Loaded += (_, _) => RecalculateStickyLayout();
            ContentScrollViewer.SizeChanged += (_, _) => RecalculateStickyLayout();
        }

        private void ContentScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            ApplyStickyOffsets();
        }

        private StickyHeaderGroup[] GetGroups() => new[]
        {
            new StickyHeaderGroup { Header = ApplicationHeader, Panel = ApplicationGroupPanel },
            new StickyHeaderGroup { Header = UIHeader, Panel = UIGroupPanel },
            new StickyHeaderGroup { Header = GameHeader, Panel = GameGroupPanel },
            new StickyHeaderGroup { Header = RenderingHeader, Panel = RenderingGroupPanel }
        };

        private void UpdateFirstHeaderMargin()
        {
            var groups = GetGroups();
            var firstVisible = groups.FirstOrDefault(g => g.Panel.Visibility == Visibility.Visible)?.Header;

            foreach (var group in groups)
            {
                if (group.Header == firstVisible)
                    group.Header.Margin = new Thickness(5, 0, 0, 0);
                else
                    group.Header.ClearValue(MarginProperty);
            }
        }

        private void LockContentWidth()
        {
            SettingsContentPanel.Width = ContentScrollViewer.ActualWidth
                - SystemParameters.VerticalScrollBarWidth
                - SettingsContentPanel.Margin.Left
                - SettingsContentPanel.Margin.Right;
        }

        private void RecalculateStickyLayout()
        {
            LockContentWidth();
            UpdateFirstHeaderMargin();

            _visibleGroups = GetGroups().Where(g => g.Panel.Visibility == Visibility.Visible).ToArray();
            if (_visibleGroups.Length == 0)
                return;

            foreach (var group in _visibleGroups)
            {
                group.Header.RenderTransform = Transform.Identity;
                group.Header.Visibility = Visibility.Visible;
                group.Header.Background = (Brush)FindResource("Window.Static.Background");
                Panel.SetZIndex(group.Header, 100);
            }

            ContentScrollViewer.UpdateLayout();

            var scrollOffset = ContentScrollViewer.VerticalOffset;
            foreach (var group in _visibleGroups)
                group.NaturalTop = group.Header.TransformToAncestor(ContentScrollViewer).Transform(new Point(0, 0)).Y + scrollOffset;

            ApplyStickyOffsets();
        }

        private void ApplyStickyOffsets()
        {
            if (_visibleGroups.Length == 0)
                return;

            var scrollOffset = ContentScrollViewer.VerticalOffset;
            var offsets = new double[_visibleGroups.Length];
            var lastStuck = -1;
            for (var i = 0; i < _visibleGroups.Length; i++)
            {
                offsets[i] = Math.Max(0, scrollOffset - _visibleGroups[i].NaturalTop);
                if (offsets[i] > 0.01)
                    lastStuck = i;
            }

            for (var i = 0; i < _visibleGroups.Length; i++)
            {
                var header = _visibleGroups[i].Header;

                if (i != lastStuck && offsets[i] > 0.01)
                {
                    header.Visibility = Visibility.Hidden;
                    continue;
                }

                header.Visibility = Visibility.Visible;
                header.RenderTransform = i == lastStuck
                    ? new TranslateTransform(0, offsets[i])
                    : Transform.Identity;
            }
        }

        private void SettingsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _visibleGroups = [];

            if (e.NewValue is TreeViewItem { Tag: SettingsNavTarget target })
            {
                ApplyFilter(target);
                ApplyGameRowFilter(null);
            }
            else if (e.NewValue is GamePathItem game)
            {
                ApplyFilter(new SettingsNavTarget { Group = GameGroupPanel });
                ApplyGameRowFilter(game);
            }
            else
            {
                ShowAll();
                ApplyGameRowFilter(null);
            }

            ContentScrollViewer.ScrollToVerticalOffset(0);
            RecalculateStickyLayout();
        }

        private void ApplyGameRowFilter(GamePathItem? selectedGame)
        {
            foreach (var item in GameFilesItemsControl.Items)
            {
                if (GameFilesItemsControl.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container)
                    container.Visibility = selectedGame == null || ReferenceEquals(item, selectedGame)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
        }

        private void SettingsTree_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestorTreeViewItem(e.OriginalSource as DependencyObject) != null)
                return;

            var selected = FindSelectedTreeViewItem(SettingsTree.Items, SettingsTree);
            if (selected != null)
                selected.IsSelected = false;
        }

        private static TreeViewItem? FindAncestorTreeViewItem(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is TreeViewItem item)
                    return item;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private static TreeViewItem? FindSelectedTreeViewItem(ItemCollection items, ItemsControl parent)
        {
            foreach (var obj in items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(obj) is not TreeViewItem container)
                    continue;

                if (container.IsSelected)
                    return container;

                var found = FindSelectedTreeViewItem(container.Items, container);
                if (found != null)
                    return found;
            }
            return null;
        }

        private void ApplyFilter(SettingsNavTarget target)
        {
            foreach (var groupPanel in GetGroups().Select(g => g.Panel))
            {
                if (groupPanel != target.Group)
                {
                    groupPanel.Visibility = Visibility.Collapsed;
                    continue;
                }

                groupPanel.Visibility = Visibility.Visible;
                foreach (var child in ((Panel)groupPanel).Children)
                {
                    if (child is not FrameworkElement settingPanel || child is TextBlock or Separator)
                        continue;

                    settingPanel.Visibility = target.Setting == null || settingPanel == target.Setting
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
        }

        private void ShowAll()
        {
            foreach (var groupPanel in GetGroups().Select(g => g.Panel))
            {
                groupPanel.Visibility = Visibility.Visible;
                foreach (var child in ((Panel)groupPanel).Children)
                {
                    if (child is FrameworkElement settingPanel)
                        settingPanel.Visibility = Visibility.Visible;
                }
            }
        }
    }

    public sealed class SettingsNavTarget
    {
        public FrameworkElement? Group { get; set; }
        public FrameworkElement? Setting { get; set; }
    }
}
