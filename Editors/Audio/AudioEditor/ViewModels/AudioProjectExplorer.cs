using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Settings.Warhammer3;
using Shared.Core.ErrorHandling;
using static Editors.Audio.AudioEditor.AudioEditorHelpers;
using static Editors.Audio.AudioEditor.AudioProjectData;
using static Editors.Audio.AudioEditor.Settings.Warhammer3.DialogueEvents;
using static Editors.Audio.AudioEditor.Settings.Warhammer3.SoundBanks;

namespace Editors.Audio.AudioEditor.ViewModels
{
    public class ActionEventSoundBanksTreeViewWrapper
    {
        public ObservableCollection<SoundBank> ActionEventSoundBanks { get; set; }
        public static string Name => "Action Events";
    }

    public class DialogueEventSoundBanksTreeViewWrapper
    {
        public ObservableCollection<SoundBank> DialogueEventSoundBanks { get; set; }
        public static string Name => "Dialogue Events";
    }

    public class MusicEventSoundBanksTreeViewWrapper
    {
        public ObservableCollection<SoundBank> MusicEventSoundBanks { get; set; }
        public static string Name => "Music Events";
    }

    public class ModdedStatesTreeViewWrapper
    {
        public ObservableCollection<StateGroup> ModdedStates { get; set; }
        public static string Name => "States";
    }

    public partial class AudioEditorViewModel
    {
        partial void OnSelectedDialogueEventPresetChanged(string value)
        {
            ApplyDialogueEventPresetFiltering();
        }

        partial void OnShowEditedSoundBanksOnlyChanged(bool value)
        {
            if (value == true)
                AddEditedSoundBanksToAudioProjectTreeViewItemsWrappers();
            else if (value == false)
                AddAllSoundBanksToAudioProjectTreeViewItemsWrappers();
        }

        partial void OnShowEditedDialogueEventsOnlyChanged(bool value)
        {
            AddEditedDialogueEventsToSoundBankTreeViewItems(_audioProjectService.AudioProject, DialogueEventSoundBankFiltering, ShowEditedDialogueEventsOnly);
        }

        [RelayCommand] public void ResetFiltering()
        {
            DialogueEventSoundBankFiltering.Clear();
            SelectedDialogueEventPreset = null;
            foreach (var soundBank in _audioProjectService.AudioProject.SoundBanks)
                soundBank.FilteredBy = null;

            AddAllDialogueEventsToSoundBankTreeViewItems(_audioProjectService.AudioProject, ShowEditedDialogueEventsOnly);
        }

        private void ApplyDialogueEventPresetFiltering()
        {
            if (_selectedAudioProjectTreeItem is SoundBank selectedSoundBank)
            {
                if (selectedSoundBank.Type == SoundBankType.DialogueEventBnk.ToString())
                {
                    if (SelectedDialogueEventPreset != null)
                    {
                        StoreDialogueEventSoundBankFiltering(selectedSoundBank.Name);
                        selectedSoundBank.FilteredBy = $" (Filtered by {SelectedDialogueEventPreset} preset)";

                        var selectedDialogueEventPreset = GetDialogueEventPreset(SelectedDialogueEventPreset);
                        AddPresetDialogueEventsToSoundBankTreeViewItems(_audioProjectService.AudioProject, selectedSoundBank.Name, selectedDialogueEventPreset, ShowEditedDialogueEventsOnly);
                    }
                }
            }
        }

        private void AddEditedSoundBanksToAudioProjectTreeViewItemsWrappers()
        {
            _audioProjectService.AudioProject.AudioProjectTreeViewItems.Clear();

            var actionEventSoundBanks = _audioProjectService.AudioProject.SoundBanks
                .Where(soundBank => soundBank.Type == SoundBankType.ActionEventBnk.ToString() 
                && soundBank.ActionEvents.Count > 0)
                .ToList();

            if (actionEventSoundBanks.Count != 0)
                AudioProjectTreeViewItems.Add(new ActionEventSoundBanksTreeViewWrapper
                {
                    ActionEventSoundBanks = new ObservableCollection<SoundBank>(actionEventSoundBanks)
                });

            var dialogueEventSoundBanks = _audioProjectService.AudioProject.SoundBanks
                .Where(soundBank => soundBank.Type == SoundBankType.DialogueEventBnk.ToString() 
                && soundBank.DialogueEvents.Any(dialogueEvent => dialogueEvent.DecisionTree.Count > 0))
                .ToList();

            if (dialogueEventSoundBanks.Count != 0)
                AudioProjectTreeViewItems.Add(new DialogueEventSoundBanksTreeViewWrapper
                {
                    DialogueEventSoundBanks = new ObservableCollection<SoundBank>(dialogueEventSoundBanks)
                });

            var musicEventSoundBanks = _audioProjectService.AudioProject.SoundBanks
                .Where(soundBank => soundBank.Type == SoundBankType.MusicEventBnk.ToString() 
                && soundBank.MusicEvents.Count > 0)
                .ToList();

            if (musicEventSoundBanks.Count != 0)
                AudioProjectTreeViewItems.Add(new MusicEventSoundBanksTreeViewWrapper
                {
                    MusicEventSoundBanks = new ObservableCollection<SoundBank>(musicEventSoundBanks)
                });

            if (_audioProjectService.AudioProject.ModdedStates.Any())
                AudioProjectTreeViewItems.Add(new ModdedStatesTreeViewWrapper
                {
                    ModdedStates = _audioProjectService.AudioProject.ModdedStates
                });
        }

        public void AddAllSoundBanksToAudioProjectTreeViewItemsWrappers()
        {
            _audioProjectService.AudioProject.AudioProjectTreeViewItems.Clear();

            var actionEventSoundBanks = _audioProjectService.AudioProject.SoundBanks
                .Where(soundBank => soundBank.Type == SoundBankType.ActionEventBnk.ToString())
                .ToList();
            if (actionEventSoundBanks.Count != 0)
                AudioProjectTreeViewItems.Add(new ActionEventSoundBanksTreeViewWrapper 
                { 
                    ActionEventSoundBanks = new ObservableCollection<SoundBank>(actionEventSoundBanks) 
                });

            var dialogueEventSoundBanks = _audioProjectService.AudioProject.SoundBanks
                .Where(soundBank => soundBank.Type == SoundBankType.DialogueEventBnk.ToString())
                .ToList();
            if (dialogueEventSoundBanks.Count != 0)
                AudioProjectTreeViewItems.Add(new DialogueEventSoundBanksTreeViewWrapper 
                { 
                    DialogueEventSoundBanks = new ObservableCollection<SoundBank>(dialogueEventSoundBanks) 
                });

            var musicEventSoundBanks = _audioProjectService.AudioProject.SoundBanks
                .Where(soundBank => soundBank.Type == SoundBankType.MusicEventBnk.ToString())
                .ToList();
            if (musicEventSoundBanks.Count != 0)
                AudioProjectTreeViewItems.Add(new MusicEventSoundBanksTreeViewWrapper 
                { 
                    MusicEventSoundBanks = new ObservableCollection<SoundBank>(musicEventSoundBanks) 
                });

            if (_audioProjectService.AudioProject.ModdedStates.Any())
                AudioProjectTreeViewItems.Add(new ModdedStatesTreeViewWrapper 
                { 
                    ModdedStates = _audioProjectService.AudioProject.ModdedStates 
                });
        }

        public void OnSelectedAudioProjectTreeViewItemChanged(object value)
        {
            if (_selectedAudioProjectTreeItem != null)
                _previousSelectedAudioProjectTreeItem = _selectedAudioProjectTreeItem;

            _selectedAudioProjectTreeItem = value;

            IsDialogueEventPresetFilterEnabled = false;
            SelectedDialogueEventPreset = null;

            if (_selectedAudioProjectTreeItem is SoundBank selectedSoundBank)
            {
                if (selectedSoundBank.Type == SoundBankType.ActionEventBnk.ToString())
                {
                    LoadActionEventSoundBankForAudioProjectEditor(selectedSoundBank);
                    LoadActionEventSoundBankForAudioProjectViewer(selectedSoundBank);

                    _logger.Here().Information($"Loaded Action Event SoundBank: {selectedSoundBank.Name}");
                }
                else if (selectedSoundBank.Type == SoundBankType.DialogueEventBnk.ToString())
                {
                    HandleDialogueEventsPresetFilter(selectedSoundBank.Name);
                }
                else if (selectedSoundBank.Type == SoundBankType.MusicEventBnk.ToString())
                {
                    throw new NotImplementedException();
                }
            }
            else if (_selectedAudioProjectTreeItem is DialogueEvent selectedDialogueEvent)
            {
                if (_audioProjectService.StateGroupsWithCustomStates == null || _audioProjectService.StateGroupsWithCustomStates.Count == 0)
                    IsShowModdedStatesCheckBoxEnabled = true;

                var areStateGroupsEqual = false;
                if (_previousSelectedAudioProjectTreeItem is DialogueEvent previousSelectedDialogueEvent)
                {
                    var newEventStateGroups = _audioRepository.DialogueEventsWithStateGroups[selectedDialogueEvent.Name];
                    var oldEventStateGroups = _audioRepository.DialogueEventsWithStateGroups[previousSelectedDialogueEvent.Name];
                    areStateGroupsEqual = newEventStateGroups.SequenceEqual(oldEventStateGroups);
                }

                GetModdedStates(_audioProjectService.AudioProject.ModdedStates, _audioProjectService.StateGroupsWithCustomStates);

                LoadDialogueEventForAudioProjectEditor(selectedDialogueEvent, ShowModdedStatesOnly, areStateGroupsEqual);
                LoadDialogueEventForAudioProjectViewer(selectedDialogueEvent, ShowModdedStatesOnly, areStateGroupsEqual);

                _logger.Here().Information($"Loaded DialogueEvent: {selectedDialogueEvent.Name}");
            }
            else if (_selectedAudioProjectTreeItem is StateGroup selectedStateGroup)
            {
                var stateGroupWithExtraUnderscores = AddExtraUnderscoresToString(selectedStateGroup.Name);

                LoadStateGroupForAudioProjectEditor(selectedStateGroup, stateGroupWithExtraUnderscores);
                LoadStateGroupForAudioProjectViewer(selectedStateGroup, stateGroupWithExtraUnderscores);

                _logger.Here().Information($"Loaded StateGroup: {selectedStateGroup.Name}");
            }

            SetIsPasteEnabled();
        }

        private void HandleDialogueEventsPresetFilter(string soundBankDisplayString)
        {
            var soundBank = GetSoundBank(soundBankDisplayString);
            DialogueEventPresets = new(DialogueEventData
                .Where(dialogueEvent => dialogueEvent.SoundBank == soundBank)
                .SelectMany(dialogueEvent => dialogueEvent.DialogueEventPreset)
                .Select(dialogueEventPreset => GetDisplayString(dialogueEventPreset))
                .Distinct()
                .OrderBy(dialogueEventPreset => dialogueEventPreset == "Show All" ? string.Empty : dialogueEventPreset)
            );

            if (DialogueEventSoundBankFiltering.TryGetValue(soundBankDisplayString, out var storedDialogueEventPreset))
                SelectedDialogueEventPreset = storedDialogueEventPreset.ToString();

            IsDialogueEventPresetFilterEnabled = true;
        }

        private void StoreDialogueEventSoundBankFiltering(string soundBank)
        {
            var selectedDialogueEventPreset = SelectedDialogueEventPreset;
            if (!DialogueEventSoundBankFiltering.TryAdd(soundBank, selectedDialogueEventPreset))
                DialogueEventSoundBankFiltering[soundBank] = selectedDialogueEventPreset;
        }
    }
}
