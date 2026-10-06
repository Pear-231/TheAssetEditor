using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AssetEditor.WindowsTitleMenu
{
    public class WindowStateToPathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var ws = (WindowState)value;
            if (ws == WindowState.Normal)
            {
                return Geometry.Parse("M 16.5,11.5 H 25.5 V 20.5 H 16.5 Z");
            }
            else
            {
                return Geometry.Parse("M 16.5,13.5 H 23.5 V 20.5 H 16.5 Z M 18.5,13.5 V 11.5 H 25.5 V 18.5 H 23.5");
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
