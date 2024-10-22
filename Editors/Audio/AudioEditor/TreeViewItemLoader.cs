using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Editors.Audio.AudioEditor.ViewModels;
using Editors.Audio.Storage;
using static Editors.Audio.AudioEditor.AudioEditorHelpers;
using static Editors.Audio.AudioEditor.DataGridConfiguration;

namespace Editors.Audio.AudioEditor
{
    public class TreeViewItemLoader
    {
        public static void HandleActionEventSoundBankSelection(AudioEditorViewModel audioEditorViewModel, IAudioRepository audioRepository, SoundBank selectedSoundBank)
        {
            LoadActionEventSoundBankForAudioProjectEditorSingleRowDataGrid(audioEditorViewModel, audioRepository, selectedSoundBank);
            LoadActionEventSoundBankForAudioProjectEditorFullDataGrid(audioEditorViewModel, audioRepository, selectedSoundBank);
        }

        public static void HandleDialogueEventSelection(AudioEditorViewModel audioEditorViewModel, IAudioRepository audioRepository, IAudioProjectService audioProjectService, DialogueEvent selectedDialogueEvent)
        {
            if (audioProjectService.StateGroupsWithModdedStatesRepository == null || audioProjectService.StateGroupsWithModdedStatesRepository.Count == 0)
                audioEditorViewModel.IsShowModdedStatesCheckBoxEnabled = true;

            var areStateGroupsEqual = false;
            if (audioEditorViewModel._previousSelectedAudioProjectTreeItem is DialogueEvent previousSelectedDialogueEvent)
            {
                var newEventStateGroups = audioRepository.DialogueEventsWithStateGroups[selectedDialogueEvent.Name];
                var oldEventStateGroups = audioRepository.DialogueEventsWithStateGroups[previousSelectedDialogueEvent.Name];
                areStateGroupsEqual = newEventStateGroups.SequenceEqual(oldEventStateGroups);
            }

            // Rebuild StateGroupsWithModdedStates in case any have been added since the Audio Project was initialised.
            audioProjectService.BuildStateGroupsWithModdedStatesRepository(audioProjectService.AudioProject.ModdedStates, audioProjectService.StateGroupsWithModdedStatesRepository);

            LoadDialogueEventForAudioProjectEditorSingleRowDataGrid(audioEditorViewModel, audioRepository, audioProjectService, selectedDialogueEvent, areStateGroupsEqual);
            LoadDialogueEventForAudioProjectEditorFullDataGrid(audioEditorViewModel, audioRepository, audioProjectService, selectedDialogueEvent, areStateGroupsEqual);
        }

        public static void HandleMusicEventSoundBankSelection()
        {
            throw new NotImplementedException();
        }

        public static void HandleStateGroupSelection(AudioEditorViewModel audioEditorViewModel, StateGroup selectedStateGroup)
        {
            var stateGroupWithExtraUnderscores = AddExtraUnderscoresToString(selectedStateGroup.Name);
            LoadStateGroupForAudioProjectEditorSingleRowDataGrid(audioEditorViewModel, selectedStateGroup, stateGroupWithExtraUnderscores);
            LoadStateGroupForAudioProjectEditorFullDataGrid(audioEditorViewModel, selectedStateGroup, stateGroupWithExtraUnderscores);
        }

        public static void LoadActionEventSoundBankForAudioProjectEditorSingleRowDataGrid(AudioEditorViewModel audioEditorViewModel, IAudioRepository audioRepository, SoundBank selectedSoundBank)
        {
            // Configure the DataGrids when necessary.
            if (selectedSoundBank.Name == "Movies" || audioEditorViewModel._previousSelectedAudioProjectTreeItem == null)
                ConfigureAudioProjectEditorSingleRowDataGridForActionEventSoundBank(audioEditorViewModel, audioRepository);
            else if (audioEditorViewModel._previousSelectedAudioProjectTreeItem is not SoundBank)
                ConfigureAudioProjectEditorSingleRowDataGridForActionEventSoundBank(audioEditorViewModel, audioRepository);
            else if (audioEditorViewModel._previousSelectedAudioProjectTreeItem is SoundBank previousSelectedSoundBank)
            {
                if (previousSelectedSoundBank.Type != Settings.Warhammer3.SoundBanks.SoundBankType.ActionEventSoundBank.ToString())
                    ConfigureAudioProjectEditorSingleRowDataGridForActionEventSoundBank(audioEditorViewModel, audioRepository);
            }

            // Clear the previous DataGrid Data.
            ClearDataGrid(audioEditorViewModel.AudioProjectEditorSingleRowDataGrid);

            // Set the format of the DataGrids.
            SetAudioProjectEditorSingleRowDataGridToActionEventSoundBank(audioEditorViewModel.AudioProjectEditorSingleRowDataGrid);
        }

        public static void LoadDialogueEventForAudioProjectEditorSingleRowDataGrid(AudioEditorViewModel audioEditorViewModel, IAudioRepository audioRepository, IAudioProjectService audioProjectService, DialogueEvent selectedDialogueEvent, bool areStateGroupsEqual = false)
        {
            // Configure the DataGrids when necessary.
            if (audioEditorViewModel.ShowModdedStatesOnly == true || areStateGroupsEqual == false || audioEditorViewModel._previousSelectedAudioProjectTreeItem == null)
                ConfigureAudioProjectEditorSingleRowDataGridForDialogueEvent(audioEditorViewModel, audioRepository, selectedDialogueEvent, audioProjectService);

            // Clear the previous DataGrid Data.
            ClearDataGrid(audioEditorViewModel.AudioProjectEditorSingleRowDataGrid);

            // Set the format of the DataGrids.
            SetAudioProjectEditorSingleRowDataGridToDialogueEvent(audioEditorViewModel.AudioProjectEditorSingleRowDataGrid, audioProjectService.DialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository, selectedDialogueEvent);
        }

        public static void LoadStateGroupForAudioProjectEditorSingleRowDataGrid(AudioEditorViewModel audioEditorViewModel, StateGroup selectedStateGroup, string stateGroupWithExtraUnderscores)
        {
            // Configure the DataGrids.
            ConfigureAudioProjectEditorSingleRowDataGridForModdedStates(audioEditorViewModel, stateGroupWithExtraUnderscores);

            // Clear the previous DataGrid Data.
            ClearDataGrid(audioEditorViewModel.AudioProjectEditorSingleRowDataGrid);

            // Set the format of the DataGrids.
            SetAudioProjectEditorSingleRowDataGridToModdedStateGroup(stateGroupWithExtraUnderscores, audioEditorViewModel.AudioProjectEditorSingleRowDataGrid);
        }

        public static void LoadActionEventSoundBankForAudioProjectEditorFullDataGrid(AudioEditorViewModel audioEditorViewModel, IAudioRepository audioRepository, SoundBank selectedSoundBank)
        {
            // Configure the DataGrids when necessary.
            if (selectedSoundBank.Name == "Movies" || audioEditorViewModel._previousSelectedAudioProjectTreeItem == null)
                ConfigureAudioProjectEditorFullDataGridForActionEventSoundBank(audioEditorViewModel, audioRepository, selectedSoundBank);
            else if (audioEditorViewModel._previousSelectedAudioProjectTreeItem is not SoundBank)
                ConfigureAudioProjectEditorFullDataGridForActionEventSoundBank(audioEditorViewModel, audioRepository, selectedSoundBank);
            else if (audioEditorViewModel._previousSelectedAudioProjectTreeItem is SoundBank previousSelectedSoundBank)
            {
                if (previousSelectedSoundBank.Type != Settings.Warhammer3.SoundBanks.SoundBankType.ActionEventSoundBank.ToString())
                    ConfigureAudioProjectEditorFullDataGridForActionEventSoundBank(audioEditorViewModel, audioRepository, selectedSoundBank);
            }

            // Clear the previous DataGrid Data.
            ClearDataGrid(audioEditorViewModel.AudioProjectEditorFullDataGrid);

            // Set the format of the DataGrids.
            SetAudioProjectEditorFullDataGridToActionEventSoundBank(audioEditorViewModel.AudioProjectEditorFullDataGrid, selectedSoundBank);
        }

        public static void LoadDialogueEventForAudioProjectEditorFullDataGrid(AudioEditorViewModel audioEditorViewModel, IAudioRepository audioRepository, IAudioProjectService audioProjectService, DialogueEvent selectedDialogueEvent, bool areStateGroupsEqual = false)
        {
            // Configure the DataGrids when necessary.
            if (audioEditorViewModel.ShowModdedStatesOnly == true || areStateGroupsEqual == false || audioEditorViewModel._previousSelectedAudioProjectTreeItem == null)
                ConfigureAudioProjectEditorFullDataGridForDialogueEvent(audioEditorViewModel, audioRepository, audioProjectService, selectedDialogueEvent);

            // Clear the previous DataGrid Data.
            ClearDataGrid(audioEditorViewModel.AudioProjectEditorFullDataGrid);

            // Set the format of the DataGrids.
            SetAudioProjectEditorFullDataGridToDialogueEvent(audioProjectService.DialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository, audioEditorViewModel.AudioProjectEditorFullDataGrid, selectedDialogueEvent);
        }

        public static void LoadStateGroupForAudioProjectEditorFullDataGrid(AudioEditorViewModel audioEditorViewModel, StateGroup selectedStateGroup, string stateGroupWithExtraUnderscores)
        {
            // Configure the DataGrids.
            ConfigureAudioProjectEditorFullDataGridForModdedStates(audioEditorViewModel, stateGroupWithExtraUnderscores);

            // Clear the previous DataGrid Data.
            ClearDataGrid(audioEditorViewModel.AudioProjectEditorFullDataGrid);

            // Set the format of the DataGrids.
            SetAudioProjectEditorFullDataGridToModdedStateGroup(audioEditorViewModel.AudioProjectEditorFullDataGrid, selectedStateGroup);
        }

        public static void SetAudioProjectEditorSingleRowDataGridToActionEventSoundBank(ObservableCollection<Dictionary<string, object>> audioProjectEditorSingleRowDataGrid)
        {
            var dataGridRow = new Dictionary<string, object> { };
            dataGridRow["Event"] = string.Empty;
            dataGridRow["AudioFiles"] = new List<string> { };
            dataGridRow["AudioFilesDisplay"] = string.Empty;
            audioProjectEditorSingleRowDataGrid.Add(dataGridRow);
        }

        public static void SetAudioProjectEditorSingleRowDataGridToDialogueEvent(ObservableCollection<Dictionary<string, object>> audioProjectEditorSingleRowDataGrid, Dictionary<string, Dictionary<string, string>> dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository, DialogueEvent dialogueEvent)
        {
            var dataGridRow = new Dictionary<string, object>();
            var stateGroupsWithQualifiers = dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository[dialogueEvent.Name];

            foreach (var kvp in stateGroupsWithQualifiers)
            {
                var stateGroupWithQualifier = kvp.Key;
                var columnHeader = AddExtraUnderscoresToString(stateGroupWithQualifier);
                dataGridRow[columnHeader] = "";
            }

            dataGridRow["AudioFiles"] = new List<string> { };
            dataGridRow["AudioFilesDisplay"] = string.Empty;
            audioProjectEditorSingleRowDataGrid.Add(dataGridRow);
        }
        public static void SetAudioProjectEditorSingleRowDataGridToModdedStateGroup(string moddedStateGroupWithExtraUnderscores, ObservableCollection<Dictionary<string, object>> audioProjectEditorSingleRowDataGrid)
        {
            var dataGridRow = new Dictionary<string, object> { };
            dataGridRow[moddedStateGroupWithExtraUnderscores] = string.Empty;
            audioProjectEditorSingleRowDataGrid.Add(dataGridRow);
        }

        public static void SetAudioProjectEditorFullDataGridToModdedStateGroup(ObservableCollection<Dictionary<string, object>> audioProjectEditorFullDataGrid, StateGroup stateGroup)
        {
            foreach (var state in stateGroup.States)
            {
                var dataGridRow = new Dictionary<string, object>();
                dataGridRow[AddExtraUnderscoresToString(stateGroup.Name)] = state.Name;
                audioProjectEditorFullDataGrid.Add(dataGridRow);
            }
        }

        public static void SetAudioProjectEditorFullDataGridToActionEventSoundBank(ObservableCollection<Dictionary<string, object>> audioProjectEditorFullDataGrid, SoundBank audioProjectItem)
        {
            foreach (var soundBankEvent in audioProjectItem.ActionEvents)
            {
                var dataGridRow = new Dictionary<string, object>();
                dataGridRow["Event"] = soundBankEvent.Name;
                dataGridRow["AudioFiles"] = soundBankEvent.AudioFiles;
                dataGridRow["AudioFilesDisplay"] = soundBankEvent.AudioFilesDisplay;
                audioProjectEditorFullDataGrid.Add(dataGridRow);
            }
        }

        public static void SetAudioProjectEditorFullDataGridToDialogueEvent(Dictionary<string, Dictionary<string, string>> dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository, ObservableCollection<Dictionary<string, object>> audioProjectEditorFullDataGrid, DialogueEvent dialogueEvent)
        {
            foreach (var decisionNode in dialogueEvent.DecisionTree)
            {
                var dataGridRow = new Dictionary<string, object>();
                dataGridRow["AudioFiles"] = decisionNode.AudioFiles;
                dataGridRow["AudioFilesDisplay"] = decisionNode.AudioFilesDisplay;

                var stateGroupsWithQualifiersList = dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository[dialogueEvent.Name].ToList();
                foreach (var (node, kvp) in decisionNode.StatePath.Nodes.Zip(stateGroupsWithQualifiersList, (node, kvp) => (node, kvp)))
                {
                    var stateGroupfromDialogueEvent = node.StateGroup.Name;
                    var stateFromDialogueEvent = node.State.Name;

                    var stateGroupWithQualifierKey = kvp.Key;
                    var stateGroup = kvp.Value;

                    if (stateGroupfromDialogueEvent == stateGroup)
                        dataGridRow[AddExtraUnderscoresToString(stateGroupWithQualifierKey)] = stateFromDialogueEvent;
                }

                audioProjectEditorFullDataGrid.Add(dataGridRow);
            }
        }
    }
}
