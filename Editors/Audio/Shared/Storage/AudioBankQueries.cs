using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Storage
{
    // Questions about the loaded banks whose shape is dictated by one screen each: modded versus
    // vanilla, grouped by bank, grouped by language.
    //
    // They lived on IAudioRepository, which meant the interface thirty-odd files hold gained a
    // member every time a screen wanted a new grouping. The index they read is unchanged and stays
    // where it is — this owns the groupings, not the storage.
    public interface IAudioBankQueries
    {
        HashSet<uint> GetUsedVanillaHircIdsByLanguageId(uint languageId);
        HashSet<uint> GetUsedVanillaSourceIdsByLanguageId(uint languageId);
        Dictionary<string, Dictionary<string, List<HircItem>>> GetVanillaDialogueEventsByBnkByLanguage();
        Dictionary<string, Dictionary<string, List<HircItem>>> GetModdedHircsByBnkByLanguage();
        Dictionary<string, List<HircItem>> GetModdedDialogueEventsByLanguage(List<string> moddedSoundBanks);
        List<string> GetModdedSoundBankFilePaths(string bnkNameSubstring);
    }

    public sealed class AudioBankQueries(IAudioRepository audioRepository) : IAudioBankQueries
    {
        // The cache can answer this from the bank index alone. Everything below it reads
        // HircsById, which loads and parses every HIRC in every resolved bank the first time it is
        // touched — a cost that used to be buried inside the repository and is now at least
        // visible from the type that pays it.
        public HashSet<uint> GetUsedVanillaHircIdsByLanguageId(uint languageId)
        {
            if (audioRepository.TryGetCachedVanillaHircIds(languageId, out var cachedHircIds))
                return cachedHircIds;

            return audioRepository.HircsById
                .SelectMany(
                    entry => entry.Value
                        .Where(hirc => hirc.LanguageId == languageId && hirc.IsCA == true)
                        .Select(_ => entry.Key))
                .ToHashSet();
        }

        public HashSet<uint> GetUsedVanillaSourceIdsByLanguageId(uint languageId)
        {
            return audioRepository.GetHircs(AkBkHircType.Sound)
                .Where(hirc => hirc.LanguageId == languageId && hirc is ICAkSound && hirc.IsCA == true)
                .Select(hirc => ((ICAkSound)hirc).GetSourceId())
                .ToHashSet();
        }

        public Dictionary<string, Dictionary<string, List<HircItem>>> GetVanillaDialogueEventsByBnkByLanguage()
        {
            return audioRepository.GetHircs(AkBkHircType.Dialogue_Event)
                .Where(hirc => hirc.IsCA)
                .GroupBy(hirc => audioRepository.GetNameFromId(hirc.LanguageId))
                .ToDictionary(
                    languageGroup => languageGroup.Key,
                    languageGroup => languageGroup
                        .GroupBy(hirc => hirc.BnkFilePath)
                        .ToDictionary(bnkGroup => bnkGroup.Key, bnkGroup => bnkGroup.ToList())
                );
        }

        public Dictionary<string, Dictionary<string, List<HircItem>>> GetModdedHircsByBnkByLanguage()
        {
            return audioRepository.HircsById
                .SelectMany(hirc => hirc.Value)
                .Where(hirc => hirc.IsCA == false)
                .GroupBy(hirc => audioRepository.GetNameFromId(hirc.LanguageId))
                .ToDictionary(
                    languageGroup => languageGroup.Key,
                    languageGroup => languageGroup
                        .GroupBy(hircItem => hircItem.BnkFilePath)
                        .ToDictionary(bnkGroup => bnkGroup.Key, bnkGroup => bnkGroup.ToList())
                );
        }

        public Dictionary<string, List<HircItem>> GetModdedDialogueEventsByLanguage(List<string> moddedSoundBanks)
        {
            return audioRepository.GetHircs(AkBkHircType.Dialogue_Event)
                .Where(hirc => hirc.IsCA == false && moddedSoundBanks.Contains(hirc.BnkFilePath))
                .GroupBy(hirc => audioRepository.GetNameFromId(hirc.LanguageId))
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        public List<string> GetModdedSoundBankFilePaths(string bnkNameSubstring)
        {
            return audioRepository.HircsById
                .SelectMany(hircDictionaryEntry => hircDictionaryEntry.Value)
                .Where(hirc => hirc.IsCA == false && hirc.BnkFilePath.Contains(bnkNameSubstring))
                .Select(hirc => hirc.BnkFilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(bnkFilePath => bnkFilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
