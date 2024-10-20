namespace Editors.Audio.AudioEditor.Settings.Warhammer3
{
    public class Languages
    {
        public enum Language
        {
            Chinese,
            EnglishUK,
            FrenchFrance,
            German,
            Italian,
            Polish,
            Russian,
            SpanishSpain
        }

        public static string GetGameString(Language language)
        {
            return language switch
            {
                Language.Chinese => "chinese",
                Language.EnglishUK => "english(uk)",
                Language.FrenchFrance => "french(france)",
                Language.German => "german",
                Language.Italian => "italian",
                Language.Polish => "polish",
                Language.Russian => "russian",
                Language.SpanishSpain => "spanish(spain)",
            };
        }
    }
}
