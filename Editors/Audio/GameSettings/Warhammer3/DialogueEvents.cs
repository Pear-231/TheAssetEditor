﻿using System.Collections.Generic;
using static Editors.Audio.GameSettings.Warhammer3.DialogueEvents.DialogueEventPreset;
using static Editors.Audio.GameSettings.Warhammer3.SoundBanks.GameSoundBank;

namespace Editors.Audio.GameSettings.Warhammer3
{
    public class DialogueEvents
    {
        public enum DialogueEventPreset
        {
            ShowAll,
            Culture,
            Lord,
            Hero,
            MountedCreature,
            LordMelee,
            LordSkirmisher,
            LordCaster,
            HeroMelee,
            HeroSkirmisher,
            HeroCaster,
            UnitInfantry,
            UnitSkirmisher,
            UnitCavalry,
            UnitSEM,
            UnitArtillery,
            City,
            PlaguesOfNurgle,
            ForbiddenWorkshop,
            Hellforge,
            MonsterPens,
            SacrificesToSotek,
            SummonTheElectorCounts,
        }

        public const string ShowAllDisplayString = "Show All";
        public const string CultureDisplayString = "Culture";
        public const string LordDisplayString = "Lord";
        public const string HeroDisplayString = "Hero";
        public const string MountedCreatureDisplayString = "Mounted Creature";
        public const string LordMeleeDisplayString = "Lord - Melee";
        public const string LordSkirmisherDisplayString = "Lord - Skirmisher";
        public const string LordCasterDisplayString = "Lord - Caster";
        public const string HeroMeleeDisplayString = "Hero - Melee";
        public const string HeroSkirmisherDisplayString = "Hero - Skirmisher";
        public const string HeroCasterDisplayString = "Hero - Caster";
        public const string UnitInfantryDisplayString = "Unit - Infantry";
        public const string UnitSkirmisherDisplayString = "Unit - Skirmisher";
        public const string UnitCavalryDisplayString = "Unit - Cavalry";
        public const string UnitSEMDisplayString = "Unit - SEM";
        public const string UnitArtilleryDisplayString = "Unit - Artillery";
        public const string CityDisplayString = "City";
        public const string PlaguesOfNurgleDisplayString = "Plagues of Nurgle";
        public const string ForbiddenWorkshopDisplayString = "Forbidden Workshop";
        public const string HellforgeDisplayString = "Hell-Forge";
        public const string MonsterPensDisplayString = "Monster Pens";
        public const string SacrificesToSotekDisplayString = "Sacrifices To Sotek";
        public const string SummonTheElectorCountsDisplayString = "Summon The Elector Counts";

        public static string GetDisplayString(DialogueEventPreset dialogueEventPreset)
        {
            return dialogueEventPreset switch
            {
                ShowAll => ShowAllDisplayString,
                Culture => CultureDisplayString,
                Lord => LordDisplayString,
                Hero => HeroDisplayString,
                MountedCreature => MountedCreatureDisplayString,
                LordMelee => LordMeleeDisplayString,
                LordSkirmisher => LordSkirmisherDisplayString,
                LordCaster => LordCasterDisplayString,
                HeroMelee => HeroMeleeDisplayString,
                HeroSkirmisher => HeroSkirmisherDisplayString,
                HeroCaster => HeroCasterDisplayString,
                UnitInfantry => UnitInfantryDisplayString,
                UnitSkirmisher => UnitSkirmisherDisplayString,
                UnitCavalry => UnitCavalryDisplayString,
                UnitSEM => UnitSEMDisplayString,
                UnitArtillery => UnitArtilleryDisplayString,
                City => CityDisplayString,
                PlaguesOfNurgle => PlaguesOfNurgleDisplayString,
                ForbiddenWorkshop => ForbiddenWorkshopDisplayString,
                Hellforge => HellforgeDisplayString,
                MonsterPens => MonsterPensDisplayString,
                SacrificesToSotek => SacrificesToSotekDisplayString,
                SummonTheElectorCounts => SummonTheElectorCountsDisplayString,
            };
        }

        public static DialogueEventPreset GetDialogueEventPreset(string dialogueEventPreset)
        {
            return dialogueEventPreset switch
            {
                ShowAllDisplayString => ShowAll,
                CultureDisplayString => Culture,
                LordDisplayString => Lord,
                HeroDisplayString => Hero,
                MountedCreatureDisplayString => MountedCreature,
                LordMeleeDisplayString => LordMelee,
                LordSkirmisherDisplayString => LordSkirmisher,
                LordCasterDisplayString => LordCaster,
                HeroMeleeDisplayString => HeroMelee,
                HeroSkirmisherDisplayString => HeroSkirmisher,
                HeroCasterDisplayString => HeroCaster,
                UnitInfantryDisplayString => UnitInfantry,
                UnitSkirmisherDisplayString => UnitSkirmisher,
                UnitCavalryDisplayString => UnitCavalry,
                UnitSEMDisplayString => UnitSEM,
                UnitArtilleryDisplayString => UnitArtillery,
                CityDisplayString => City,
                PlaguesOfNurgleDisplayString => PlaguesOfNurgle,
                ForbiddenWorkshopDisplayString => ForbiddenWorkshop,
                HellforgeDisplayString => Hellforge,
                MonsterPensDisplayString => MonsterPens,
                SacrificesToSotekDisplayString => SacrificesToSotek,
                SummonTheElectorCountsDisplayString => SummonTheElectorCounts,
            };
        }

        // Dialogue Event data has to be defined directly rather than dynamically from game data as it can only be determined by examining how CA uses each Dialogue Event in game
        public static List<(string Name, SoundBanks.GameSoundBank SoundBank, DialogueEventPreset[] DialogueEventPreset, bool Recommended)> DialogueEventData { get; } =
        [
            // Frontend VO
            ("frontend_vo_character_select", FrontendVO, [ShowAll, Lord], true),

            // Campaign VO
            ("campaign_vo_agent_action_failed", CampaignVO, [ShowAll, Hero], true),
            ("campaign_vo_agent_action_success", CampaignVO, [ShowAll, Hero], true),
            ("campaign_vo_attack", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cam_disband", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_cam_disbanded_neg", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_cam_disbanded_pos", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_cam_skill_weapon_tree", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_cam_skill_weapon_tree_response", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_cam_tech_tree", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_cam_tech_tree_response", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_created", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_diplomacy_negative", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_diplomacy_positive", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_diplomacy_selected", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_level_up", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_mounted_creature", CampaignVO, [MountedCreature], true),
            ("campaign_vo_move", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_move_garrisoning", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_move_next_turn", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_new_commander", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_no", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_no_short", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_post_battle_defeat", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_post_battle_victory", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_recruit_units", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_retreat", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_selected", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_selected_allied", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_selected_fail", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_selected_first_time", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_selected_neutral", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_selected_short", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_ship_dock", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_special_ability", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_stance_ambush", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_astromancy", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_channeling", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_default", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_double_time", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_land_raid", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_march", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_muster", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_patrol", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_raise_dead", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_set_camp", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_set_camp_raiding", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_settle", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_stalking", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_stance_tunneling", CampaignVO, [ShowAll, Lord], true),
            ("campaign_vo_yes", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_yes_short", CampaignVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_yes_short_aggressive", CampaignVO, [ShowAll, Lord, Hero], true),
            ("gotrek_felix_arrival", CampaignVO, [ShowAll, Lord], true),
            ("gotrek_felix_departure", CampaignVO, [ShowAll, Lord], true),

            // Campaign Conversational VO
            ("Campaign_CS_Nur_Plague_Infect", CampaignConversationalVO, [ShowAll, PlaguesOfNurgle], true),
            ("Campaign_CS_Nur_Plague_Summon_Cultist", CampaignConversationalVO, [ShowAll, PlaguesOfNurgle], true),
            ("campaign_vo_cs_city_buildings_damaged", CampaignConversationalVO, [ShowAll, Lord, Hero, Culture], false),
            ("campaign_vo_cs_city_high_corruption", CampaignConversationalVO, [ShowAll, Lord, Hero, Culture], false),
            ("campaign_vo_cs_city_other_generic", CampaignConversationalVO, [ShowAll, Lord, Hero, Culture], false),
            ("campaign_vo_cs_city_own_generic", CampaignConversationalVO, [ShowAll, Lord, Hero, Culture], false),
            ("campaign_vo_cs_city_public_order_low", CampaignConversationalVO, [ShowAll, Lord, Hero, Culture], false),
            ("campaign_vo_cs_city_riot", CampaignConversationalVO, [ShowAll, Lord, Hero, Culture], false),
            ("campaign_vo_cs_city_under_siege", CampaignConversationalVO, [ShowAll, Lord, Hero, Culture], false),
            ("campaign_vo_cs_confident", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_enemy_region_generic", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_forbidden_workshop_purchase_doomrocket", CampaignConversationalVO, [ShowAll, ForbiddenWorkshop], true),
            ("campaign_vo_cs_forbidden_workshop_upgrade_doomflayer", CampaignConversationalVO, [ShowAll, ForbiddenWorkshop], true),
            ("campaign_vo_cs_forbidden_workshop_upgrade_doomwheel", CampaignConversationalVO, [ShowAll, ForbiddenWorkshop], true),
            ("campaign_vo_cs_forbidden_workshop_upgrade_weapon_teams", CampaignConversationalVO, [ShowAll, ForbiddenWorkshop], true),
            ("campaign_vo_cs_hellforge_accept", CampaignConversationalVO, [ShowAll, Hellforge], true),
            ("campaign_vo_cs_hellforge_customisation_category", CampaignConversationalVO, [ShowAll, Hellforge], true),
            ("campaign_vo_cs_hellforge_customisation_unit", CampaignConversationalVO, [ShowAll, Hellforge], true),
            ("campaign_vo_cs_in_forest", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_in_mountains", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_in_rain", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_in_snow", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_intimidated", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_monster_pens_dilemma_ghrond", CampaignConversationalVO, [ShowAll, MonsterPens], true),
            ("campaign_vo_cs_monster_pens_dilemma_lustria", CampaignConversationalVO, [ShowAll, MonsterPens], true),
            ("campaign_vo_cs_monster_pens_dilemma_naggaroth", CampaignConversationalVO, [ShowAll, MonsterPens], true),
            ("campaign_vo_cs_monster_pens_dilemma_old_world", CampaignConversationalVO, [ShowAll, MonsterPens], true),
            ("campaign_vo_cs_monster_pens_event", CampaignConversationalVO, [ShowAll, MonsterPens], true),
            ("campaign_vo_cs_near_sea", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_neutral", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_on_sea", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_other_character_details_panel_low_loyalty", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_other_character_details_panel_neutral", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_other_character_details_panel_positive", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_post_battle_captives_enslave", CampaignConversationalVO, [ShowAll, Lord, Culture], true),
            ("campaign_vo_cs_post_battle_captives_execute", CampaignConversationalVO, [ShowAll, Lord, Culture], true),
            ("campaign_vo_cs_post_battle_captives_release", CampaignConversationalVO, [ShowAll, Lord, Culture], true),
            ("campaign_vo_cs_post_battle_close_defeat", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_post_battle_close_victory", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_post_battle_defeat", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_post_battle_great_defeat", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_post_battle_great_victory", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_post_battle_settlement_do_nothing", CampaignConversationalVO, [ShowAll, Lord, Culture], true),
            ("campaign_vo_cs_post_battle_settlement_establish_foreign_slot", CampaignConversationalVO, [ShowAll, Lord, Culture], true),
            ("campaign_vo_cs_post_battle_settlement_loot", CampaignConversationalVO, [ShowAll, Lord, Culture], true),
            ("campaign_vo_cs_post_battle_settlement_occupy", CampaignConversationalVO, [ShowAll, Lord, Culture], true),
            ("campaign_vo_cs_post_battle_settlement_occupy_factory", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_post_battle_settlement_occupy_outpost", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_post_battle_settlement_occupy_tower", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_post_battle_settlement_raze", CampaignConversationalVO, [ShowAll, Lord, Culture], true),
            ("campaign_vo_cs_post_battle_settlement_reinstate_elector_count", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_post_battle_settlement_sack", CampaignConversationalVO, [ShowAll, Lord, Culture], true),
            ("campaign_vo_cs_post_battle_settlement_vassal_enlist", CampaignConversationalVO, [ShowAll, Lord, Culture], true),
            ("campaign_vo_cs_post_battle_victory", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_pre_battle_fight_battle", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_pre_battle_retreat", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_pre_battle_siege_break", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_pre_battle_siege_continue", CampaignConversationalVO, [ShowAll, Lord], true),
            ("campaign_vo_cs_proximity", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_sacrifice_to_sotek", CampaignConversationalVO, [ShowAll, SacrificesToSotek], true),
            ("campaign_vo_cs_sea_storm", CampaignConversationalVO, [ShowAll, Lord, Hero, Culture], true),
            ("campaign_vo_cs_spam_click", CampaignConversationalVO, [ShowAll, Lord, Hero], true),
            ("campaign_vo_cs_summon_elector_counts_panel_open_vo", CampaignConversationalVO, [ShowAll], true),
            ("campaign_vo_cs_tzarkan_calls_and_taunts", CampaignConversationalVO, [ShowAll], true),
            ("campaign_vo_cs_tzarkan_whispers", CampaignConversationalVO, [ShowAll], true),
            ("campaign_vo_cs_weather_cold", CampaignConversationalVO, [ShowAll], true),
            ("campaign_vo_cs_weather_hot", CampaignConversationalVO, [ShowAll], true),
            ("campaign_vo_cs_wef_daiths_forge", CampaignConversationalVO, [ShowAll], true),

            // Battle VO
            ("battle_vo_order_attack", BattleVO, [ShowAll], true),
            ("battle_vo_order_attack_alternative", BattleVO, [ShowAll], true),
            ("battle_vo_order_bat_mode_capture_neg", BattleVO, [ShowAll], true),
            ("battle_vo_order_bat_mode_capture_pos", BattleVO, [ShowAll], true),
            ("battle_vo_order_bat_mode_survival", BattleVO, [ShowAll], true),
            ("battle_vo_order_bat_speeches", BattleVO, [ShowAll], true),
            ("battle_vo_order_battle_continue_battle", BattleVO, [ShowAll], true),
            ("battle_vo_order_battle_quit_battle", BattleVO, [ShowAll], true),
            ("battle_vo_order_change_ammo", BattleVO, [ShowAll], true),
            ("battle_vo_order_change_formation", BattleVO, [ShowAll], true),
            ("battle_vo_order_climb", BattleVO, [ShowAll], true),
            ("battle_vo_order_fire_at_will_off", BattleVO, [ShowAll], true),
            ("battle_vo_order_fire_at_will_on", BattleVO, [ShowAll], true),
            ("battle_vo_order_flying_charge", BattleVO, [ShowAll], true),
            ("battle_vo_order_formation_lock", BattleVO, [ShowAll], true),
            ("battle_vo_order_formation_unlock", BattleVO, [ShowAll], true),
            ("battle_vo_order_generic_response", BattleVO, [ShowAll], true),
            ("battle_vo_order_group_created", BattleVO, [ShowAll], true),
            ("battle_vo_order_group_disbanded", BattleVO, [ShowAll], true),
            ("battle_vo_order_guard_off", BattleVO, [ShowAll], true),
            ("battle_vo_order_guard_on", BattleVO, [ShowAll], true),
            ("battle_vo_order_halt", BattleVO, [ShowAll], true),
            ("battle_vo_order_man_siege_tower", BattleVO, [ShowAll], true),
            ("battle_vo_order_melee_off", BattleVO, [ShowAll], true),
            ("battle_vo_order_melee_on", BattleVO, [ShowAll], true),
            ("battle_vo_order_move", BattleVO, [ShowAll], true),
            ("battle_vo_order_move_alternative", BattleVO, [ShowAll], true),
            ("battle_vo_order_move_ram", BattleVO, [ShowAll], true),
            ("battle_vo_order_move_siege_tower", BattleVO, [ShowAll], true),
            ("battle_vo_order_pick_up_engine", BattleVO, [ShowAll], true),
            ("battle_vo_order_select", BattleVO, [ShowAll], true),
            ("battle_vo_order_short_order", BattleVO, [ShowAll], true),
            ("battle_vo_order_skirmish_off", BattleVO, [ShowAll], true),
            ("battle_vo_order_skirmish_on", BattleVO, [ShowAll], true),
            ("battle_vo_order_special_ability", BattleVO, [ShowAll], true),
            ("battle_vo_order_withdraw", BattleVO, [ShowAll], true),
            ("battle_vo_order_withdraw_tactical", BattleVO, [ShowAll], true),

            // Battle Conversational VO
            ("battle_vo_conversation_allied_unit_routing", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_clash", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_def_own_army_murderous_prowess_100_percent", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_def_own_army_murderous_prowess_75_percent", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_dissapointment", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_encouragement", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_army_at_chokepoint", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_army_black_arks_triggered", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_army_has_many_cannons", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_skaven_unit_revealed", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_unit_at_rear", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_unit_charging", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_unit_chariot_charge", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_unit_dragon", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_unit_flanking", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_unit_flying", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_unit_large_creature", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_unit_revealed", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_enemy_unit_spell_cast", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_environment_ground_type_forest", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_environment_ground_type_mud", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_environment_in_cave", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_environment_in_water", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_environment_weather_cold", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_environment_weather_desert", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_environment_weather_rain", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_environment_weather_snow", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_hef_own_army_air_units", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_hef_own_army_low_stength", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_lzd_own_army_dino_rampage", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_army_at_chokepoint", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_army_black_arks_triggered", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_army_caused_damage", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_army_missile_amount_inferior", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_army_missile_amount_superior", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_army_peasants_fleeing", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_army_spell_cast", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_unit_artillery_fire", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_unit_artillery_firing", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_unit_artillery_reload", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_unit_fearful", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_unit_moving", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_unit_routing", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_unit_under_dragon_firebreath_attack", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_unit_under_ranged_attack", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_own_unit_wavering", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_proximity", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_siege_attack", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_siege_defence", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_skv_own_unit_spawn_units", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_skv_own_unit_tactical_withdraw", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_skv_own_unit_warpfire_artillery", BattleConversationalVO, [ShowAll], true),
            ("battle_vo_conversation_storm_of_magic", BattleConversationalVO, [ShowAll], true),

            // Battle Individual Melee
            ("Battle_Individual_Melee_Weapon_Hit", BattleIndividualMelee, [ShowAll], true),
        ];
    }
}