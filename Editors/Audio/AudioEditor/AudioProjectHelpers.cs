using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Editors.Audio.AudioEditor.DataGrids;
using Editors.Audio.AudioEditor.Models;
using Editors.Audio.AudioEditor.Settings;
using Editors.Audio.GameSettings.Warhammer3;
using Editors.Audio.Storage;
using static Editors.Audio.AudioEditor.Settings.Settings;

namespace Editors.Audio.AudioEditor
{
    public class AudioProjectHelpers
    {
        public static string GetStateGroupFromStateGroupWithQualifier(IAudioRepository audioRepository, string dialogueEvent, string stateGroupWithQualifier)
        {
            if (audioRepository.QualifiedStateGroupLookupByStateGroupByDialogueEvent.TryGetValue(dialogueEvent, out var stateGroupDictionary))
                if (stateGroupDictionary.TryGetValue(stateGroupWithQualifier, out var stateGroup))
                    return stateGroup;

            return null;
        }

        public static ActionEvent GetActionEventFromRow(AudioProject audioProject, DataRow row)
        {
            var actionEventName = GetActionEventNameFromRow(row);
            return audioProject.SoundBanks
                .Where(soundBank => soundBank.SoundBankType == SoundBanks.Wh3SoundBankType.ActionEventSoundBank)
                .SelectMany(soundBank => soundBank.ActionEvents)
                .FirstOrDefault(actionEvent => actionEvent.Name == actionEventName);
        }

        public static StatePath GetStatePathFromRow(IAudioRepository audioRepository, DialogueEvent selectedDialogueEvent, DataRow row)
        {
            // TODO: Figure out how this function actually is used?
            // CA sometimes add new State Groups into a Dialogue Event
            // When that Dialogue Event already contains State Paths the new State Group's value in the path is empty
            // So we skip any state groups with empty values
            var rowStatePathNodes = new List<StatePathNode>();
            foreach (DataColumn column in row.Table.Columns)
            {
                var valueObject = row[column];
                if (valueObject == DBNull.Value)
                    continue;

                var stateName = valueObject.ToString();
                if (string.IsNullOrEmpty(stateName))
                    continue;

                var stateGroupColumnName = DataGridHelpers.DeduplicateUnderscores(column.ColumnName);

                rowStatePathNodes.Add(new StatePathNode
                {
                    StateGroup = new StateGroup { Name = GetStateGroupFromStateGroupWithQualifier(audioRepository, selectedDialogueEvent.Name, stateGroupColumnName) },
                    State = new State { Name = stateName }
                });
            }

            foreach (var statePath in selectedDialogueEvent.StatePaths)
            {
                if (statePath.Nodes.SequenceEqual(rowStatePathNodes, new StatePathNodeComparer()))
                    return statePath;
            }

            return null;
        }

        public static string GetValueFromRow(DataRow row, string columnName)
        {
            return row[columnName].ToString();
        }

        public static string GetActionEventNameFromRow(DataRow row)
        {
            return GetValueFromRow(row, DataGridTemplates.EventColumn);
        }

        public static string GetStateNameFromRow(DataRow row)
        {
            return GetValueFromRow(row, DataGridTemplates.StateColumn);
        }

        public static StatePath CreateStatePathFromStatePathNodes(IAudioRepository audioRepository, List<StatePathNode> statePathNodes, List<AudioFile> audioFiles)
        {
            var statePath = new StatePath();

            foreach (var statePathNode in statePathNodes)
            {
                statePath.Nodes.Add(new StatePathNode
                {
                    StateGroup = new StateGroup { Name = statePathNode.StateGroup.Name },
                    State = new State { Name = statePathNode.State.Name }
                });

                if (audioFiles.Count == 1)
                {
                    statePath.Sound = CreateSound(audioFiles[0]);
                    statePath.Sound.AudioSettings = new AudioSettings();
                }
                else
                {
                    statePath.RandomSequenceContainer = new RandomSequenceContainer
                    {
                        Sounds = [],
                        AudioSettings = BuildRecommendedRanSeqContainerSettings(audioFiles)
                    };

                    foreach (var audioFile in audioFiles)
                    {
                        var sound = CreateSound(audioFile);
                        statePath.RandomSequenceContainer.Sounds.Add(sound);
                    }
                }
            }

            return statePath;
        }

        private static Sound CreateSound(AudioFile audioFile)
        {
            var sound = new Sound()
            {
                WavFileName = audioFile.FileName,
                WavFilePath = audioFile.FilePath,
            };

            return sound;
        }

        public static ISettings GetSettingsFromActionEvent(AudioProject audioProject, DataRow row)
        {
            var actionEvent = GetActionEventFromRow(audioProject, row);

            if (actionEvent.RandomSequenceContainer != null)
                return actionEvent.RandomSequenceContainer.AudioSettings;
            else
                return actionEvent.Sound.AudioSettings;
        }

        public static ISettings GetSettingsFromStatePath(IAudioEditorService audioEditorService, IAudioRepository audioRepository)
        {
            var audioProjectItem = audioEditorService.SelectedExplorerNode;
            var selectedViewerRow = audioEditorService.SelectedViewerRows[0];
            var dialogueEvent = audioEditorService.AudioProject.GetDialogueEvent(audioEditorService.SelectedExplorerNode.Name);
            var statePath = GetStatePathFromRow(audioRepository, dialogueEvent, selectedViewerRow);

            if (statePath.RandomSequenceContainer != null)
                return statePath.RandomSequenceContainer.AudioSettings;
            else
                return statePath.Sound.AudioSettings;
        }

        public static void InsertStatePathAlphabetically(DialogueEvent selectedDialogueEvent, StatePath statePath)
        {
            var newStateName = statePath.Nodes.First().State.Name;
            var decisionTree = selectedDialogueEvent.StatePaths;
            var insertIndex = 0;

            for (var i = 0; i < decisionTree.Count; i++)
            {
                var existingStateName = decisionTree[i].Nodes.First().State.Name;
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

            decisionTree.Insert(insertIndex, statePath);
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

        public static AudioSettings BuildSoundSettings(AudioSettings storedSoundSettings)
        {
            var soundSettings = new AudioSettings();
            soundSettings.LoopingType = storedSoundSettings.LoopingType;
            if (storedSoundSettings.LoopingType == LoopingType.FiniteLooping)
                soundSettings.NumberOfLoops = storedSoundSettings.NumberOfLoops;
            return soundSettings;
        }

        public static RandomSequenceContainerSettings BuildRanSeqContainerSettings(RandomSequenceContainerSettings storedRanSeqContainerSettings)
        {
            var ranSeqContainerSettings = new RandomSequenceContainerSettings();

            ranSeqContainerSettings.PlaylistType = storedRanSeqContainerSettings.PlaylistType;

            if (storedRanSeqContainerSettings.PlaylistType == PlaylistType.Sequence)
                ranSeqContainerSettings.EndBehaviour = storedRanSeqContainerSettings.EndBehaviour;
            else
            {
                ranSeqContainerSettings.EnableRepetitionInterval = storedRanSeqContainerSettings.EnableRepetitionInterval;

                if (storedRanSeqContainerSettings.EnableRepetitionInterval)
                    ranSeqContainerSettings.RepetitionInterval = storedRanSeqContainerSettings.RepetitionInterval;
            }

            ranSeqContainerSettings.AlwaysResetPlaylist = storedRanSeqContainerSettings.AlwaysResetPlaylist;

            ranSeqContainerSettings.PlaylistMode = storedRanSeqContainerSettings.PlaylistMode;
            ranSeqContainerSettings.LoopingType = storedRanSeqContainerSettings.LoopingType;

            if (storedRanSeqContainerSettings.LoopingType == LoopingType.FiniteLooping)
                ranSeqContainerSettings.NumberOfLoops = storedRanSeqContainerSettings.NumberOfLoops;

            if (storedRanSeqContainerSettings.TransitionType != TransitionType.Disabled)
            {
                ranSeqContainerSettings.TransitionType = storedRanSeqContainerSettings.TransitionType;
                ranSeqContainerSettings.TransitionDuration = storedRanSeqContainerSettings.TransitionDuration;
            }

            return ranSeqContainerSettings;
        }

        public static RandomSequenceContainerSettings BuildRecommendedRanSeqContainerSettings(List<AudioFile> audioFiles)
        {
            var ranSeqContainerSettings = new RandomSequenceContainerSettings();
            ranSeqContainerSettings.PlaylistType = PlaylistType.RandomExhaustive;
            ranSeqContainerSettings.EnableRepetitionInterval = true;
            ranSeqContainerSettings.RepetitionInterval = (uint)Math.Ceiling(audioFiles.Count / 2.0);
            ranSeqContainerSettings.EndBehaviour = EndBehaviour.Restart;
            ranSeqContainerSettings.AlwaysResetPlaylist = true;
            ranSeqContainerSettings.PlaylistMode = PlaylistMode.Step;
            ranSeqContainerSettings.LoopingType = LoopingType.Disabled;
            ranSeqContainerSettings.NumberOfLoops = 1;
            ranSeqContainerSettings.TransitionType = TransitionType.Disabled;
            ranSeqContainerSettings.TransitionDuration = 1;
            return ranSeqContainerSettings;
        }
    }
}
