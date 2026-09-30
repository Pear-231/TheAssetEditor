using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common;

namespace AssetEditor.Views.Settings
{
    public class SettingsEnumConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return value;
            else if (value is GameTypeEnum game)
                return GetGameLocalisation(game);
            else if (value is ThemeType theme)
                return GetThemeLocalisation(theme);
            else if (value is BackgroundColour backgroundColour)
                return GetBackgroundColourLocalisation(backgroundColour);
            else if (value is CameraControlMode cameraControlMode)
                return GetCameraControlModeLocalisation(cameraControlMode);
            else
                return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }

        private static string GetGameLocalisation(GameTypeEnum game)
        {
            var key = game switch
            {
                GameTypeEnum.Warhammer => "SettingsWindow.GameType.Warhammer",
                GameTypeEnum.Warhammer2 => "SettingsWindow.GameType.Warhammer2",
                GameTypeEnum.Warhammer3 => "SettingsWindow.GameType.Warhammer3",
                GameTypeEnum.Troy => "SettingsWindow.GameType.Troy",
                GameTypeEnum.ThreeKingdoms => "SettingsWindow.GameType.ThreeKingdoms",
                GameTypeEnum.Rome2 => "SettingsWindow.GameType.Rome2",
                GameTypeEnum.Attila => "SettingsWindow.GameType.Attila",
                GameTypeEnum.Pharaoh => "SettingsWindow.GameType.Pharaoh",
                _ => throw new NotImplementedException(),
            };
            return GetLocalisation(key);
        }

        private static string GetThemeLocalisation(ThemeType theme)
        {
            var key = theme switch
            {
                ThemeType.DarkTheme => "SettingsWindow.Theme.Dark",
                ThemeType.LightTheme => "SettingsWindow.Theme.Light",
                _ => throw new NotImplementedException(),
            };
            return GetLocalisation(key);
        }

        private static string GetBackgroundColourLocalisation(BackgroundColour colour)
        {
            var key = colour switch
            {
                BackgroundColour.DarkGrey => "SettingsWindow.RenderBackground.DarkGrey",
                BackgroundColour.LegacyBlue => "SettingsWindow.RenderBackground.LegacyBlue",
                BackgroundColour.Green => "SettingsWindow.RenderBackground.Green",
                _ => throw new NotImplementedException(),
            };
            return GetLocalisation(key);
        }

        private static string GetCameraControlModeLocalisation(CameraControlMode mode)
        {
            var key = mode switch
            {
                CameraControlMode.BlenderStyle => "SettingsWindow.CameraMode.BlenderStyle",
                CameraControlMode.AssetEditorStyle => "SettingsWindow.CameraMode.AssetEditorStyle",
                _ => throw new NotImplementedException(),
            };
            return GetLocalisation(key);
        }

        private static string GetLocalisation(string key)
        {
            if (Application.Current is IAssetEditorMain appMain)
                return appMain.ServiceProvider.GetRequiredService<LocalizationManager>().Get(key);

            return key;
        }
    }
}
