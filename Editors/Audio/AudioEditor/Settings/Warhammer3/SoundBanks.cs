namespace Editors.Audio.AudioEditor.Settings.Warhammer3
{
    public class SoundBanks
    {
        public enum SoundBank
        {
            Abilities,
            CampaignAdvisor,
            DiplomacyLines,
            EventNarration,
            Magic,
            Movies,
            QuestBattles,
            Rituals,
            UI,
            Vocalisation,
            BattleIndividualMelee,
            BattleConversationalVO,
            BattleVO,
            CampaignConversationalVO,
            CampaignVO,
            FrontendVO,
            BattleMusic,
            CampaignMusic,
            LoadingScreenMusic
        }

        public enum SoundBankType
        {
            ActionEventBnk,
            DialogueEventBnk,
            MusicEventBnk
        }

        public static SoundBankType GetSoundBankType(SoundBank soundbank)
        {
            return soundbank switch
            {
                SoundBank.Abilities => SoundBankType.ActionEventBnk,
                SoundBank.CampaignAdvisor => SoundBankType.ActionEventBnk,
                SoundBank.DiplomacyLines => SoundBankType.ActionEventBnk,
                SoundBank.EventNarration => SoundBankType.ActionEventBnk,
                SoundBank.Magic => SoundBankType.ActionEventBnk,
                SoundBank.Movies => SoundBankType.ActionEventBnk,
                SoundBank.QuestBattles => SoundBankType.ActionEventBnk,
                SoundBank.Rituals => SoundBankType.ActionEventBnk,
                SoundBank.UI => SoundBankType.ActionEventBnk,
                SoundBank.Vocalisation => SoundBankType.ActionEventBnk,
                SoundBank.BattleIndividualMelee => SoundBankType.DialogueEventBnk,
                SoundBank.BattleConversationalVO => SoundBankType.DialogueEventBnk,
                SoundBank.BattleVO => SoundBankType.DialogueEventBnk,
                SoundBank.CampaignConversationalVO => SoundBankType.DialogueEventBnk,
                SoundBank.CampaignVO => SoundBankType.DialogueEventBnk,
                SoundBank.FrontendVO => SoundBankType.DialogueEventBnk,
                SoundBank.BattleMusic => SoundBankType.MusicEventBnk,
                SoundBank.CampaignMusic => SoundBankType.MusicEventBnk,
                SoundBank.LoadingScreenMusic => SoundBankType.MusicEventBnk,
            };
        }

        public const string AbilitiesDisplayString = "Abilities";
        public const string CampaignAdvisorDisplayString = "Campaign Advisor";
        public const string DiplomacyLinesDisplayString = "Diplomacy Lines";
        public const string EventNarrationDisplayString = "Event Narration";
        public const string MagicDisplayString = "Magic";
        public const string MoviesDisplayString = "Movies";
        public const string QuestBattlesDisplayString = "Quest Battles";
        public const string RitualsDisplayString = "Rituals";
        public const string UIDisplayString = "UI";
        public const string VocalisationDisplayString = "Vocalisation";
        public const string BattleIndividualMeleeDisplayString = "Battle Individual Melee";
        public const string BattleConversationalVODisplayString = "Battle Conversational VO";
        public const string BattleVODisplayString = "Battle VO";
        public const string CampaignConversationalVODisplayString = "Campaign Conversational VO";
        public const string CampaignVODisplayString = "Campaign VO";
        public const string FrontendVODisplayString = "Frontend VO";
        public const string BattleMusicDisplayString = "Battle Music";
        public const string CampaignMusicDisplayString = "Campaign Music";
        public const string LoadingScreenMusicDisplayString = "Loading Screen Music";

        public static string GetDisplayString(SoundBank soundbank)
        {
            return soundbank switch
            {
                SoundBank.Abilities => AbilitiesDisplayString,
                SoundBank.CampaignAdvisor => CampaignAdvisorDisplayString,
                SoundBank.DiplomacyLines => DiplomacyLinesDisplayString,
                SoundBank.EventNarration => EventNarrationDisplayString,
                SoundBank.Magic => MagicDisplayString,
                SoundBank.Movies => MoviesDisplayString,
                SoundBank.QuestBattles => QuestBattlesDisplayString,
                SoundBank.Rituals => RitualsDisplayString,
                SoundBank.UI => UIDisplayString,
                SoundBank.Vocalisation => VocalisationDisplayString,
                SoundBank.FrontendVO => FrontendVODisplayString,
                SoundBank.CampaignVO => CampaignVODisplayString,
                SoundBank.CampaignConversationalVO => CampaignConversationalVODisplayString,
                SoundBank.BattleVO => BattleVODisplayString,
                SoundBank.BattleConversationalVO => BattleConversationalVODisplayString,
                SoundBank.BattleIndividualMelee => BattleIndividualMeleeDisplayString,
                SoundBank.BattleMusic => BattleMusicDisplayString,
                SoundBank.CampaignMusic => CampaignMusicDisplayString,
                SoundBank.LoadingScreenMusic => LoadingScreenMusicDisplayString,
            };
        }

        public static SoundBank GetSoundBank(string soundBankString)
        {
            return soundBankString switch
            {
                AbilitiesDisplayString => SoundBank.Abilities,
                CampaignAdvisorDisplayString => SoundBank.CampaignAdvisor,
                DiplomacyLinesDisplayString => SoundBank.DiplomacyLines,
                EventNarrationDisplayString => SoundBank.EventNarration,
                MagicDisplayString => SoundBank.Magic,
                MoviesDisplayString => SoundBank.Movies,
                QuestBattlesDisplayString => SoundBank.QuestBattles,
                RitualsDisplayString => SoundBank.Rituals,
                UIDisplayString => SoundBank.UI,
                VocalisationDisplayString => SoundBank.Vocalisation,
                BattleConversationalVODisplayString => SoundBank.BattleConversationalVO,
                BattleIndividualMeleeDisplayString => SoundBank.BattleIndividualMelee,
                BattleVODisplayString => SoundBank.BattleVO,
                CampaignConversationalVODisplayString => SoundBank.CampaignConversationalVO,
                CampaignVODisplayString => SoundBank.CampaignVO,
                FrontendVODisplayString => SoundBank.FrontendVO,
                BattleMusicDisplayString => SoundBank.BattleMusic,
                CampaignMusicDisplayString => SoundBank.CampaignMusic,
                LoadingScreenMusicDisplayString => SoundBank.LoadingScreenMusic,
            };
        }
    }
}
