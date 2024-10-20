using System;
using System.Globalization;
using System.Windows.Data;
using Editors.Audio.AudioEditor.Settings.Warhammer3;

namespace Editors.Audio.AudioEditor.Converters
{
    public class SettingsEnumConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return value;
            else if (value is Languages.Language language)
                return Languages.GetGameString(language);
            else if (value is SoundBanks.SoundBank soundbank)
                return SoundBanks.GetDisplayString(soundbank);
            else if (value is DialogueEvents.DialogueEventPreset dialogueEventSubtype)
                return DialogueEvents.GetDisplayString(dialogueEventSubtype);
            else
                return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
