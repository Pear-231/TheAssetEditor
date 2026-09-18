using System.Globalization;
using System.Windows.Data;
using Shared.Core.Settings;
using static Shared.Core.Settings.ApplicationSettingsHelper;
using static Shared.Core.Settings.ThemesController;

namespace AssetEditor.Views.Settings
{
    public class SettingsEnumConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return value;
            else if (value is GameTypeEnum game)
                return GameInformationDatabase.GetEnumAsString(game);
            else if (value is ThemeType theme)
                return GetEnumAsString(theme);
            else if (value is BackgroundColour backgroundColour)
                return GetEnumAsString(backgroundColour);
            else if (value is CameraControlMode cameraControlMode)
                return GetEnumAsString(cameraControlMode);
            else
                return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
