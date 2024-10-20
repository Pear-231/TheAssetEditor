using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.Audio.AudioEditor.Settings.Warhammer3;
using static Editors.Audio.AudioEditor.Settings.Warhammer3.DialogueEvents;
using static Editors.Audio.AudioEditor.Settings.Warhammer3.SoundBanks;
using static Editors.Audio.AudioEditor.Settings.Warhammer3.StateGroups;

namespace Editors.Audio.AudioEditor
{
    public abstract class IAudioProjectItem : ObservableObject
    {
        public string Name { get; set; }
    }

    public partial class SoundBank : IAudioProjectItem
    {
        [ObservableProperty] public string _filteredBy;
        public string Type { get; set; }
        public ObservableCollection<ActionEvent> ActionEvents { get; set; } = [];
        public ObservableCollection<DialogueEvent> DialogueEvents { get; set; } = [];
        public ObservableCollection<MusicEvent> MusicEvents { get; set; } = [];
        [JsonIgnore] public ObservableCollection<object> SoundBankTreeViewItems { get; set; } = [];
    }

    public class ActionEvent : IAudioProjectItem
    {
        public List<string> AudioFiles { get; set; } = [];
        public string AudioFilesDisplay { get; set; }
    }

    public class DialogueEvent : IAudioProjectItem
    {
        public List<DecisionNode> DecisionTree { get; set; } = [];
    }

    public class MusicEvent : IAudioProjectItem
    {
        public List<string> AudioFiles { get; set; } = [];
        public string AudioFilesDisplay { get; set; }
    }

    public class StateGroup : IAudioProjectItem 
    {
        public List<State> States { get; set; } = [];
    }

    public class State : IAudioProjectItem { }

    public class DecisionNode
    {
        public StatePath StatePath { get; set; }
        public List<string> AudioFiles { get; set; } = [];
        public string AudioFilesDisplay { get; set; }
    }

    public class StatePath
    {
        public List<StatePathNode> Nodes { get; set; } = [];
    }

    public class StatePathNode
    {
        public StateGroup StateGroup { get; set; }
        public State State { get; set; }
    }

    public class AudioProjectData
    {
        public string FileName { get; set; }
        public string Directory { get; set; }
        public string Language { get; set; }
        public ObservableCollection<SoundBank> SoundBanks { get; set; } = [];
        public ObservableCollection<StateGroup> ModdedStates { get; set; } = [];
        [JsonIgnore] public ObservableCollection<object> AudioProjectTreeViewItems { get; set; } = [];

        public static void InitialiseSoundBanks(AudioProjectData audioProject)
        {
            var soundBanks = Enum.GetValues<SoundBanks.SoundBank>()
                .Select(soundBank => new SoundBank
                {
                    Name = GetDisplayString(soundBank),
                    Type = GetSoundBankType(soundBank).ToString(),
                    DialogueEvents = new ObservableCollection<DialogueEvent>()
                })
                .ToList();

            foreach (var soundBank in soundBanks)
            {
                audioProject.SoundBanks.Add(soundBank);

                var dialogueEvents = DialogueEventData.Where(dialogueEvent => dialogueEvent.SoundBank == GetSoundBank(soundBank.Name))
                    .Select(dialogueEvent => new DialogueEvent
                    {
                        Name = dialogueEvent.Name
                    });

                foreach (var dialogueEvent in dialogueEvents)
                    soundBank.DialogueEvents.Add(dialogueEvent);
            }
        }

        public static void InitialiseModdedStatesGroups(ObservableCollection<StateGroup> moddedStates)
        {
            foreach (var moddedStateGroup in ModdedStateGroups)
            {
                var stateGroup = new StateGroup { Name = moddedStateGroup };
                moddedStates.Add(stateGroup);
            }
        }

        public static void AddAllDialogueEventsToSoundBankTreeViewItems(AudioProjectData audioProject, bool showEditedDialogueEventsOnly)
        {
            foreach (var soundBank in audioProject.SoundBanks)
            {
                soundBank.SoundBankTreeViewItems.Clear();

                if (soundBank.DialogueEvents != null)
                {
                    if (showEditedDialogueEventsOnly == true)
                    {
                        var editedDialogueEvents = soundBank.DialogueEvents
                            .Where(dialogueEvent => dialogueEvent.DecisionTree.Count > 0)
                            .ToList();

                        foreach (var dialogueEvent in editedDialogueEvents)
                            if (!soundBank.SoundBankTreeViewItems.Contains(dialogueEvent))
                                soundBank.SoundBankTreeViewItems.Add(dialogueEvent);
                    }
                    else
                    {
                        foreach (var dialogueEvent in soundBank.DialogueEvents)
                            if (!soundBank.SoundBankTreeViewItems.Contains(dialogueEvent))
                                soundBank.SoundBankTreeViewItems.Add(dialogueEvent);
                    }
                }
            }
        }

        public static void AddPresetDialogueEventsToSoundBankTreeViewItems(AudioProjectData audioProject, string targetSoundBank, DialogueEventPreset dialogueEventPreset, bool showEditedDialogueEventsOnly)
        {
            foreach (var soundBank in audioProject.SoundBanks)
            {
                if (soundBank.Name == targetSoundBank)
                {
                    soundBank.SoundBankTreeViewItems.Clear();

                    if (soundBank.DialogueEvents != null)
                    {
                        var presetDialogueEvents = DialogueEventData
                            .Where(dialogueEvent => GetDisplayString(dialogueEvent.SoundBank) == targetSoundBank
                            && dialogueEvent.DialogueEventPreset.Contains(dialogueEventPreset))
                            .Select(dialogueEvent => dialogueEvent.Name);

                        if (showEditedDialogueEventsOnly == true)
                        {
                            var editedDialogueEvents = soundBank.DialogueEvents
                                .Where(dialogueEvent => dialogueEvent.DecisionTree.Count > 0)
                                .ToList();

                            foreach (var dialogueEvent in editedDialogueEvents)
                                if (presetDialogueEvents.Contains(dialogueEvent.Name) && !(soundBank.SoundBankTreeViewItems.Contains(dialogueEvent)))
                                    soundBank.SoundBankTreeViewItems.Add(dialogueEvent);
                        }
                        else
                        {
                            foreach (var dialogueEvent in soundBank.DialogueEvents)
                                if (presetDialogueEvents.Contains(dialogueEvent.Name) && !(soundBank.SoundBankTreeViewItems.Contains(dialogueEvent)))
                                    soundBank.SoundBankTreeViewItems.Add(dialogueEvent);
                        }
                    }
                }
            }
        }

        public static void AddEditedDialogueEventsToSoundBankTreeViewItems(AudioProjectData audioProject, Dictionary<string, string> dialogueEventFiltering, bool showEditedDialogueEventsOnly)
        {
            foreach (var soundBank in audioProject.SoundBanks)
            {
                soundBank.SoundBankTreeViewItems.Clear();

                if (soundBank.DialogueEvents != null)
                {
                    if (showEditedDialogueEventsOnly == true)
                    {
                        var editedDialogueEvents = soundBank.DialogueEvents
                            .Where(dialogueEvent => dialogueEvent.DecisionTree.Count > 0)
                            .ToList();

                        if (dialogueEventFiltering.Keys.ToList().Contains(soundBank.Name))
                        {
                            var presetDialogueEvents = new List<string>();
                            foreach (var dialogueEventData in DialogueEventData)
                            {
                                if (dialogueEventFiltering.TryGetValue(GetDisplayString(dialogueEventData.SoundBank), out var dialogueEventPreset))
                                    if (dialogueEventData.DialogueEventPreset.Contains(GetDialogueEventPreset(dialogueEventPreset)))
                                        presetDialogueEvents.Add(dialogueEventData.Name);
                            }

                            foreach (var dialogueEvent in editedDialogueEvents)
                                if (presetDialogueEvents.Contains(dialogueEvent.Name) && !(soundBank.SoundBankTreeViewItems.Contains(dialogueEvent)))
                                    soundBank.SoundBankTreeViewItems.Add(dialogueEvent);
                        }
                        else
                        {
                            foreach (var dialogueEvent in editedDialogueEvents)
                                if (!soundBank.SoundBankTreeViewItems.Contains(dialogueEvent))
                                    soundBank.SoundBankTreeViewItems.Add(dialogueEvent);
                        }
                    }
                    else
                    {
                        if (dialogueEventFiltering.Keys.ToList().Contains(soundBank.Name))
                        {
                            var presetDialogueEvents = new List<string>();
                            foreach (var dialogueEventData in DialogueEventData)
                            {
                                if (dialogueEventFiltering.TryGetValue(GetDisplayString(dialogueEventData.SoundBank), out var dialogueEventPreset))
                                    if (dialogueEventData.DialogueEventPreset.Contains(GetDialogueEventPreset(dialogueEventPreset)))
                                        presetDialogueEvents.Add(dialogueEventData.Name);
                            }

                            foreach (var dialogueEvent in soundBank.DialogueEvents)
                                if (presetDialogueEvents.Contains(dialogueEvent.Name) && !(soundBank.SoundBankTreeViewItems.Contains(dialogueEvent)))
                                    soundBank.SoundBankTreeViewItems.Add(dialogueEvent);
                        }
                        else
                        {
                            foreach (var dialogueEvent in soundBank.DialogueEvents)
                                if (!soundBank.SoundBankTreeViewItems.Contains(dialogueEvent))
                                    soundBank.SoundBankTreeViewItems.Add(dialogueEvent);
                        }
                    }
                }
            }
        }
    }        
}
