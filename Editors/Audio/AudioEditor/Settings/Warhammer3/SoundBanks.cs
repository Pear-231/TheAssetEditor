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
            ActionEventSoundBank,
            DialogueEventSoundBank,
            MusicEventSoundBank
        }

        public static SoundBankType GetSoundBankType(SoundBank soundbank)
        {
            return soundbank switch
            {
                SoundBank.Abilities => SoundBankType.ActionEventSoundBank,
                SoundBank.CampaignAdvisor => SoundBankType.ActionEventSoundBank,
                SoundBank.DiplomacyLines => SoundBankType.ActionEventSoundBank,
                SoundBank.EventNarration => SoundBankType.ActionEventSoundBank,
                SoundBank.Magic => SoundBankType.ActionEventSoundBank,
                SoundBank.Movies => SoundBankType.ActionEventSoundBank,
                SoundBank.QuestBattles => SoundBankType.ActionEventSoundBank,
                SoundBank.Rituals => SoundBankType.ActionEventSoundBank,
                SoundBank.UI => SoundBankType.ActionEventSoundBank,
                SoundBank.Vocalisation => SoundBankType.ActionEventSoundBank,
                SoundBank.BattleIndividualMelee => SoundBankType.DialogueEventSoundBank,
                SoundBank.BattleConversationalVO => SoundBankType.DialogueEventSoundBank,
                SoundBank.BattleVO => SoundBankType.DialogueEventSoundBank,
                SoundBank.CampaignConversationalVO => SoundBankType.DialogueEventSoundBank,
                SoundBank.CampaignVO => SoundBankType.DialogueEventSoundBank,
                SoundBank.FrontendVO => SoundBankType.DialogueEventSoundBank,
                SoundBank.BattleMusic => SoundBankType.MusicEventSoundBank,
                SoundBank.CampaignMusic => SoundBankType.MusicEventSoundBank,
                SoundBank.LoadingScreenMusic => SoundBankType.MusicEventSoundBank,
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
