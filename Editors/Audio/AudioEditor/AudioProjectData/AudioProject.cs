using System;
using System.Collections.Generic;
using System.Linq;
using Shared.GameFormats.Wwise.Enums;
using static Editors.Audio.AudioEditor.AudioSettings.AudioSettings;
using static Editors.Audio.GameSettings.Warhammer3.DialogueEvents;
using static Editors.Audio.GameSettings.Warhammer3.SoundBanks;
using static Editors.Audio.GameSettings.Warhammer3.StateGroups;

namespace Editors.Audio.AudioEditor.AudioProjectData
{
    public class AudioProject
    {
        public string Language { get; set; }
        public List<SoundBank> SoundBanks { get; set; }
        public List<StateGroup> StateGroups { get; set; }

        public static AudioProject CreateAudioProject()
        {
            // TODO: Add abstraction for other games
            var audioProject = new AudioProject();
            InitialiseSoundBanks(audioProject);
            InitialiseModdedStatesGroups(audioProject);
            return audioProject;
        }

        private static void InitialiseSoundBanks(AudioProject audioProject)
        {
            var soundBanks = Enum.GetValues<Wh3SoundBankSubtype>()
                .Select(soundBankSubtype => new SoundBank
                {
                    Name = GetSoundBankSubTypeString(soundBankSubtype),
                    SoundBankType = GetSoundBankSubType(soundBankSubtype)
                })
                .ToList();

            audioProject.SoundBanks = [];

            foreach (var soundBankSubtype in Enum.GetValues<Wh3SoundBankSubtype>())
            {
                var soundBank = new SoundBank
                {
                    Name = GetSoundBankSubTypeString(soundBankSubtype),
                    SoundBankType = GetSoundBankSubType(soundBankSubtype)
                };

                if (soundBank.SoundBankType == Wh3SoundBankType.ActionEventSoundBank)
                    soundBank.ActionEvents = [];
                else
                {
                    soundBank.DialogueEvents = [];

                    var filteredDialogueEvents = DialogueEventData
                        .Where(dialogueEvent => dialogueEvent.SoundBank == GetSoundBankSubtype(soundBank.Name));

                    foreach (var dialogueData in filteredDialogueEvents)
                    {
                        var dialogueEvent = new DialogueEvent
                        {
                            Name = dialogueData.Name,
                            StatePaths = []
                        };
                        soundBank.DialogueEvents.Add(dialogueEvent);
                    }
                }

                audioProject.SoundBanks.Add(soundBank);
            }

            SortSoundBanksAlphabetically(audioProject);
        }

        private static void InitialiseModdedStatesGroups(AudioProject audioProject)
        {
            audioProject.StateGroups = [];

            foreach (var moddedStateGroup in ModdedStateGroups)
            {
                var stateGroup = new StateGroup { Name = moddedStateGroup, States = [] };
                audioProject.StateGroups.Add(stateGroup);
            }
        }

        private static void SortSoundBanksAlphabetically(AudioProject audioProject)
        {
            var sortedSoundBanks = audioProject.SoundBanks.OrderBy(soundBank => soundBank.Name).ToList();

            audioProject.SoundBanks.Clear();

            foreach (var soundBank in sortedSoundBanks)
                audioProject.SoundBanks.Add(soundBank);
        }
    }

    public abstract class AudioProjectItem
    {
        public string Name { get; set; }
        public uint ID { get; set; }
    }

    public abstract class AudioProjectHircItem : AudioProjectItem
    {
        public abstract AkBkHircType HircType { get; set; }
    }

    public partial class SoundBank : AudioProjectItem
    {
        public Wh3SoundBankType SoundBankType { get; set; }
        public Wh3SoundBankSubtype SoundBankSubtype { get; set; }
        public List<ActionEvent> ActionEvents { get; set; }
        public List<DialogueEvent> DialogueEvents { get; set; }
        public string Language { get; set; }
        public string SoundBankFileName { get; set; }
        public string SoundBankFilePath { get; set; }
    }

    public class ActionEvent : AudioProjectHircItem
    {
        public override AkBkHircType HircType { get; set; } = AkBkHircType.Event;

        // Technically we should make each action contain the SoundContainer / Sound but making multiple actions for an event isn't supported as the user probably doesn't need to.
        public List<Action> Actions { get; set; } 
        public RandomSequenceContainer RandomSequenceContainer { get; set; }
        public Sound Sound { get; set; }
    }

    public class Action : AudioProjectHircItem
    {
        public override AkBkHircType HircType { get; set; } = AkBkHircType.Action;
        public AkActionType ActionType { get; set; } = AkActionType.Play;
        public uint IDExt { get; set; }
    }

    public class DialogueEvent : AudioProjectHircItem
    {
        public override AkBkHircType HircType { get; set; } = AkBkHircType.Dialogue_Event;
        public List<StatePath> StatePaths { get; set; }
    }

    public class StateGroup : AudioProjectItem
    {
        public List<State> States { get; set; }
    }

    public class State : AudioProjectItem { }

    public class StatePath
    {
        public List<StatePathNode> Nodes { get; set; } = [];
        public RandomSequenceContainer RandomSequenceContainer { get; set; }
        public Sound Sound { get; set; }
    }

    public class StatePathNode
    {
        public StateGroup StateGroup { get; set; }
        public State State { get; set; }
    }

    public class RandomSequenceContainer : AudioProjectHircItem
    {
        public override AkBkHircType HircType { get; set; } = AkBkHircType.RandomSequenceContainer;
        public uint OverrideBusID { get; set; }
        public uint DirectParentID { get; set; }
        public RanSeqContainerSettings AudioSettings { get; set; }
        public List<Sound> Sounds { get; set; }
        public string Language { get; set; }
    }

    public class Sound : AudioProjectHircItem
    {
        public override AkBkHircType HircType { get; set; } = AkBkHircType.Sound;
        public uint OverrideBusID { get; set; }
        public uint DirectParentID { get; set; }
        public uint SourceID { get; set; }
        public string WavFileName { get; set; }
        public string WavFilePath { get; set; }
        public string WemFileName { get; set; }
        public string WemFilePath { get; set; }
        public string WemDiskFilePath { get; set; }
        public long InMemoryMediaSize { get; set; }
        public string Language { get; set; }
        public SoundSettings AudioSettings { get; set; }
    }

    public interface IAudioSettings { }

    public class RanSeqContainerSettings : IAudioSettings
    {
        public PlaylistType PlaylistType { get; set; }
        public bool EnableRepetitionInterval { get; set; }
        public uint RepetitionInterval { get; set; }
        public EndBehaviour EndBehaviour { get; set; }
        public bool AlwaysResetPlaylist { get; set; }
        public PlaylistMode PlaylistMode { get; set; }
        public LoopingType LoopingType { get; set; }
        public uint NumberOfLoops { get; set; }
        public TransitionType TransitionType { get; set; }
        public decimal TransitionDuration { get; set; }
    }

    public class SoundSettings : IAudioSettings
    {
        public LoopingType LoopingType { get; set; }
        public uint NumberOfLoops { get; set; }
    }
}
