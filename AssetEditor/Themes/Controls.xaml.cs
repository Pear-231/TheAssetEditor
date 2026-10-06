using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using WindowHandling;

namespace AssetEditor.Themes
{
    public partial class Controls
    {
        private static readonly ILogger s_logger = Logging.Create<Controls>();

        // WPF's Placement="Right" doesn't position a submenu Popup flush against its parent MenuItem.
        // The parent item is inset from its own popup's edge by the popup's border and padding, and the submenu's
        // first item is inset by the same amount from the submenu's top. Placing the submenu at the parent item's
        // top-right, shifted out and up by that inset, makes the two popups sit flush with the items aligned. The submenu's left
        // border overlaps the parent popup's right border by one border thickness so the two borders read as one line.
        private const double PopupInset = 5;
        private const double PopupBorderThickness = 1;

        public static CustomPopupPlacementCallback SubmenuRightPlacementCallback { get; } = GetSubmenuRightPlacements;

        private static CustomPopupPlacement[] GetSubmenuRightPlacements(Size popupSize, Size targetSize, Point offset)
        {
            var parentTopRight = new Point(targetSize.Width + PopupInset - PopupBorderThickness, -PopupInset);
            var placement = new CustomPopupPlacement(parentTopRight, PopupPrimaryAxis.Horizontal);
            return [placement];
        }

        private void CloseWindow_Event(object sender, RoutedEventArgs e)
        {
            if (e.Source != null)
                this.CloseWind(Window.GetWindow((FrameworkElement)e.Source));
        }

        private void AutoMinimize_Event(object sender, RoutedEventArgs e)
        {
            if (e.Source != null)
                this.MaximizeRestore(Window.GetWindow((FrameworkElement)e.Source));
        }

        private void Minimize_Event(object sender, RoutedEventArgs e)
        {
            if (e.Source != null)
                this.MinimizeWind(Window.GetWindow((FrameworkElement)e.Source));
        }

        private void Help_Event(object sender, RoutedEventArgs e)
        {
            if (e.Source == null)
                return;

            var window = Window.GetWindow((FrameworkElement)e.Source) as AssetEditorWindow;
            if (window == null)
                return;

            if (string.IsNullOrWhiteSpace(window.HelpDocumentPath))
            {
                MessageBox.Show("No documentation is currently available for this tool", "Documentation Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rawPath = window.HelpDocumentPath;
            var pathSuffix = "";
            var queryIndex = rawPath.IndexOf('?');
            var fragmentIndex = rawPath.IndexOf('#');
            var suffixIndex = -1;
            if (queryIndex >= 0 && fragmentIndex >= 0)
                suffixIndex = Math.Min(queryIndex, fragmentIndex);
            else if (queryIndex >= 0)
                suffixIndex = queryIndex;
            else if (fragmentIndex >= 0)
                suffixIndex = fragmentIndex;

            if (suffixIndex >= 0)
            {
                pathSuffix = rawPath.Substring(suffixIndex);
                rawPath = rawPath.Substring(0, suffixIndex);
            }

            var helpPath = Path.IsPathRooted(rawPath)
                ? rawPath
                : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rawPath));

            if (!File.Exists(helpPath) && Debugger.IsAttached)
            {
                s_logger.Here().Information("Help file not found at '{HelpPath}', searching parent directories", helpPath);
                var searchDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (searchDir?.Parent != null)
                {
                    searchDir = searchDir.Parent;
                    var candidate = Path.Combine(searchDir.FullName, rawPath);
                    if (File.Exists(candidate))
                    {
                        helpPath = candidate;
                        break;
                    }
                }
            }

            if (!File.Exists(helpPath))
            {
                s_logger.Here().Warning("Help file not found: '{HelpPath}'", helpPath);
                MessageBox.Show("No documentation is currently available for this tool", "Documentation Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fileUri = new Uri(helpPath).AbsoluteUri + pathSuffix;
            s_logger.Here().Information("Opening help document: {Uri}", fileUri);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{fileUri}\"",
                UseShellExecute = true
            });
        }

        public void CloseWind(Window window) => window?.Close();

        public void MaximizeRestore(Window window)
        {
            if (window == null)
                return;
            switch (window.WindowState)
            {
                case WindowState.Normal:
                    window.WindowState = WindowState.Maximized;
                    break;
                case WindowState.Minimized:
                case WindowState.Maximized:
                    window.WindowState = WindowState.Normal;
                    break;
            }
        }

        public void MinimizeWind(Window window)
        {
            if (window != null)
                window.WindowState = WindowState.Minimized;
        }
    }
}
