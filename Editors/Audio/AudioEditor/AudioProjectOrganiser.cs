using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Editors.Audio.AudioEditor
{
    public class AudioProjectOrganiser
    {
        public static void SortSoundBanksAlphabetically(ObservableCollection<SoundBank> audioProjectItems)
        {
            var sortedSoundBanks = audioProjectItems.OrderBy(soundBank => soundBank.Name).ToList();

            audioProjectItems.Clear();

            foreach (var soundBank in sortedSoundBanks)
                audioProjectItems.Add(soundBank);
        }

        public static void InsertDataGridRowAlphabetically(ObservableCollection<Dictionary<string, object>> audioProjectEditorFullDataGrid, Dictionary<string, object> newRow)
        {
            var insertIndex = 0;
            var newValue = newRow.First().Value.ToString();

            for (var i = 0; i < audioProjectEditorFullDataGrid.Count; i++)
            {
                var currentValue = audioProjectEditorFullDataGrid[i].First().Value.ToString();
                var comparison = string.Compare(newValue, currentValue, StringComparison.Ordinal);
                if (comparison < 0)
                {
                    insertIndex = i;
                    break;
                }
                else if (comparison == 0)
                    insertIndex = i + 1;
                else
                    insertIndex = i + 1;
            }

            audioProjectEditorFullDataGrid.Insert(insertIndex, newRow);
        }

        public static void InsertStatePathAlphabetically(DialogueEvent selectedDialogueEvent, DecisionNode decisionNode, StatePath newStatePath)
        {
            var newStateName = newStatePath.Nodes.First().State.Name;
            var decisionTree = selectedDialogueEvent.DecisionTree;
            var insertIndex = 0;

            for (var i = 0; i < decisionTree.Count; i++)
            {
                var existingStateName = decisionTree[i].StatePath.Nodes.First().State.Name;
                var comparison = string.Compare(newStateName, existingStateName, StringComparison.Ordinal);
                if (comparison < 0)
                {
                    insertIndex = i;
                    break;
                }
                else if (comparison == 0)
                    insertIndex = i + 1;
                else
                    insertIndex = i + 1;
            }

            decisionTree.Insert(insertIndex, decisionNode);
        }

        public static void InsertActionEventAlphabetically(SoundBank selectedSoundBank, ActionEvent newEvent)
        {
            var events = selectedSoundBank.ActionEvents;
            var newEventName = newEvent.Name;
            var insertIndex = 0;

            for (var i = 0; i < events.Count; i++)
            {
                var existingEventName = events[i].Name;
                var comparison = string.Compare(newEventName, existingEventName, StringComparison.Ordinal);
                if (comparison < 0)
                {
                    insertIndex = i;
                    break;
                }
                else if (comparison == 0)
                    insertIndex = i + 1;
                else
                    insertIndex = i + 1;
            }

            events.Insert(insertIndex, newEvent);
        }

        public static void InsertStateAlphabetically(StateGroup moddedStateGroup, State newState)
        {
            var states = moddedStateGroup.States;
            var newStateName = newState.Name;
            var insertIndex = 0;

            for (var i = 0; i < states.Count; i++)
            {
                var existingStateName = states[i].Name;
                var comparison = string.Compare(newStateName, existingStateName, StringComparison.Ordinal);

                if (comparison < 0)
                {
                    insertIndex = i;
                    break;
                }
                else if (comparison == 0)
                    insertIndex = i + 1;
                else
                    insertIndex = i + 1;
            }

            states.Insert(insertIndex, newState);
        }
    }
}
