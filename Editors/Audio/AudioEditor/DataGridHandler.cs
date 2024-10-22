using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using static Editors.Audio.AudioEditor.AudioEditorHelpers;
using static Editors.Audio.AudioEditor.AudioProjectOrganiser;
using static Editors.Audio.AudioEditor.TreeViewItemLoader;

namespace Editors.Audio.AudioEditor
{
    public class DataGridHandler
    {
        public static Dictionary<string, object> ExtractRowFromDataGrid(ObservableCollection<Dictionary<string, object>> audioProjectEditorSingleRowDataGrid, object selectedAudioProjectTreeItem)
        {
            var newRow = new Dictionary<string, object>();

            foreach (var kvp in audioProjectEditorSingleRowDataGrid[0])
            {
                var columnName = kvp.Key;
                var cellValue = kvp.Value;
                if (columnName == "AudioFiles" && cellValue is List<string> stringList)
                {
                    var newList = new List<string>(stringList);
                    newRow[columnName] = newList;
                }
                else
                {
                    if (selectedAudioProjectTreeItem is DialogueEvent)
                    {
                        if (cellValue.ToString() == string.Empty && columnName != "AudioFilesDisplay")
                        {
                            newRow[columnName] = "Any";
                            continue;
                        }
                    }
                    newRow[columnName] = cellValue.ToString();
                }
            }

            return newRow;
        }

        public static void AddDataGridRowToActionEventSoundBank(Dictionary<string, object> dataGridRow, SoundBank selectedSoundBank)
        {
            var soundBankEvent = new ActionEvent();

            foreach (var kvp in dataGridRow)
            {
                if (kvp.Key == "AudioFiles")
                {
                    var filePaths = kvp.Value as List<string>;
                    var fileNames = filePaths.Select(Path.GetFileName);
                    var fileNamesString = string.Join(", ", fileNames);

                    soundBankEvent.AudioFiles = filePaths;
                    soundBankEvent.AudioFilesDisplay = fileNamesString;
                }
                else if (kvp.Key != "AudioFiles" && kvp.Key != "AudioFilesDisplay")
                    soundBankEvent.Name = kvp.Value.ToString();
            }

            InsertActionEventAlphabetically(selectedSoundBank, soundBankEvent);
        }

        public static void AddDataGridRowToDialogueEvent(Dictionary<string, object> dataGridRow, DialogueEvent selectedDialogueEvent, Dictionary<string, Dictionary<string, string>> dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository)
        {
            var decisionNode = new DecisionNode();
            var statePath = new StatePath();

            foreach (var kvp in dataGridRow)
            {
                if (kvp.Key == "AudioFiles")
                {
                    var filePaths = kvp.Value as List<string>;
                    var fileNames = filePaths.Select(Path.GetFileName);
                    var fileNamesString = string.Join(", ", fileNames);

                    decisionNode.AudioFiles = filePaths;
                    decisionNode.AudioFilesDisplay = fileNamesString;
                }
                else if (kvp.Key != "AudioFiles" && kvp.Key != "AudioFilesDisplay")
                {
                    var stateGroupWithQualifierAndExtraUnderscores = kvp.Key;
                    var stateGroupWithQualifier = RemoveExtraUnderscoresFromString(stateGroupWithQualifierAndExtraUnderscores);

                    var statePathNode = new StatePathNode
                    {
                        StateGroup = new StateGroup(),
                        State = new State()
                    };

                    statePathNode.StateGroup.Name = GetStateGroupFromStateGroupWithQualifier(selectedDialogueEvent.Name, stateGroupWithQualifier, dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository);
                    statePathNode.State.Name = kvp.Value.ToString();

                    statePath.Nodes.Add(statePathNode);
                }
            }

            decisionNode.StatePath = statePath;

            InsertStatePathAlphabetically(selectedDialogueEvent, decisionNode, statePath);
        }

        public static void AddDataGridRowToModdedStates(Dictionary<string, object> dataGridRow, StateGroup stateGroup)
        {
            var state = new State();

            foreach (var kvp in dataGridRow)
                state.Name = kvp.Value.ToString();

            InsertStateAlphabetically(stateGroup, state);
        }

        public static void RemoveDataGridRowFromDialogueEvent(ObservableCollection<Dictionary<string, object>> audioProjectEditorFullDataGrid, Dictionary<string, object> dataGridRow, DialogueEvent selectedDialogueEvent, Dictionary<string, Dictionary<string, string>> dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository)
        {
            var statePath = new StatePath();

            foreach (var kvp in dataGridRow)
            {
                if (kvp.Key != "AudioFiles" && kvp.Key != "AudioFilesDisplay")
                {
                    var stateGroupWithQualifierAndExtraUnderscores = kvp.Key;
                    var stateGroupWithQualifier = RemoveExtraUnderscoresFromString(stateGroupWithQualifierAndExtraUnderscores);
                    var state = kvp.Value;

                    var statePathNode = new StatePathNode { };
                    statePathNode.StateGroup.Name = GetStateGroupFromStateGroupWithQualifier(selectedDialogueEvent.Name, stateGroupWithQualifier, dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository);
                    statePathNode.State.Name = state.ToString();

                    statePath.Nodes.Add(statePathNode);
                }
            }

            var matchingStatePath = GetMatchingDecisionNode(statePath, selectedDialogueEvent);
            if (matchingStatePath != null)
            {
                selectedDialogueEvent.DecisionTree.Remove(matchingStatePath);
                audioProjectEditorFullDataGrid.Remove(dataGridRow);
            }
        }

        public static void HandleAddingActionEventDataGridRow(Dictionary<string, object> newRow, SoundBank soundBank, ObservableCollection<Dictionary<string, object>> audioProjectEditorSingleRowDataGrid)
        {
            // Reset row format.
            SetAudioProjectEditorSingleRowDataGridToActionEventSoundBank(audioProjectEditorSingleRowDataGrid);

            AddDataGridRowToActionEventSoundBank(newRow, soundBank);
        }

        public static void HandleAddingDialogueEventDataGridRow(Dictionary<string, object> newRow, DialogueEvent selectedDalogueEvent, ObservableCollection<Dictionary<string, object>> audioProjectEditorSingleRowDataGrid, Dictionary<string, Dictionary<string, string>> dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository)
        {            
            // Reset row format.
            SetAudioProjectEditorSingleRowDataGridToDialogueEvent(audioProjectEditorSingleRowDataGrid, dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository, selectedDalogueEvent);
            
            AddDataGridRowToDialogueEvent(newRow, selectedDalogueEvent, dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository);
        }

        public static void HandleAddingMusicEventDataGridRow()
        {
            throw new NotImplementedException();
        }

        public static void HandleAddingStateGroupDataGridRow(Dictionary<string, object> newRow, StateGroup moddedStateGroup, ObservableCollection<Dictionary<string, object>> audioProjectEditorSingleRowDataGrid)
        {
            // Reset row format.
            SetAudioProjectEditorSingleRowDataGridToModdedStateGroup(moddedStateGroup.Name, audioProjectEditorSingleRowDataGrid);
            
            AddDataGridRowToModdedStates(newRow, moddedStateGroup);
        }
    }
}
