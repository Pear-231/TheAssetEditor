using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace AssetEditor.ViewModels
{
    public class RecentPackFileItem
    {
        public RecentPackFileItem(string path, PackFileContainerType containerType, bool isReadOnly, LocalizationManager localizationManager, Action execute)
        {
            Command = new RelayCommand(execute);
            Path = path;
            Header = $"{System.IO.Path.GetFileName(path)} {BuildKindLabel(containerType, isReadOnly, localizationManager)}";
        }

        private static string BuildKindLabel(PackFileContainerType containerType, bool isReadOnly, LocalizationManager localizationManager)
        {
            var typeText = containerType switch
            {
                PackFileContainerType.Pack => localizationManager.Get("PackFile.Flag.Pack"),
                PackFileContainerType.Project => localizationManager.Get("PackFile.Flag.Project"),
                PackFileContainerType.GamePacks => localizationManager.Get("PackFile.Flag.GamePacks"),
                _ => containerType.ToString()
            };
            return isReadOnly ? $"[{typeText}, {localizationManager.Get("PackFile.Flag.ReadOnly")}]" : $"[{typeText}]";
        }

        public string Header { get; set; }
        public string Path { get; set; }

        public ICommand Command { get; }
    }
}
