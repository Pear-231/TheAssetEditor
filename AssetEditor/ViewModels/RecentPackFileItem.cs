using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace AssetEditor.ViewModels
{
    public class RecentPackFileItem
    {
        public RecentPackFileItem(string path, bool isSystemFolder, Action execute)
        {
            Command = new RelayCommand(execute);
            Header = path;
            IsSystemFolder = isSystemFolder;
        }

        public string Header { get; set; }

        public bool IsSystemFolder { get; set; }

        public ICommand Command { get; }
    }
}
