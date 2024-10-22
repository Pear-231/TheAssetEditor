using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CommonControls.PackFileBrowser;
using Editors.Audio.AudioEditor.Settings.Warhammer3;
using Editors.Audio.AudioEditor.ViewModels;
using Editors.Audio.Storage;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using static Editors.Audio.AudioEditor.AudioEditorHelpers;
using static Editors.Audio.AudioEditor.AudioProjectOrganiser;
using static Editors.Audio.AudioEditor.DialogueEventFilter;
using static Editors.Audio.AudioEditor.Settings.Warhammer3.DialogueEvents;
using static Editors.Audio.AudioEditor.Settings.Warhammer3.SoundBanks;
using static Editors.Audio.AudioEditor.Settings.Warhammer3.StateGroups;
using static Editors.Audio.AudioEditor.TreeViewWrapper;

namespace Editors.Audio.AudioEditor
{
    public interface IAudioProjectService
    {
        AudioProjectData AudioProject { get; set; }
        Dictionary<string, List<string>> StateGroupsWithModdedStatesRepository { get; set; }
        Dictionary<string, Dictionary<string, string>> DialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository { get; set; }
        void SaveAudioProject(PackFileService packFileService);
        void LoadAudioProject(PackFileService packFileService, IAudioRepository audioRepository, AudioEditorViewModel audioEditorViewModel);
        void InitialiseAudioProject(AudioEditorViewModel audioEditorViewModel, string fileName, string directory, string language);
        void BuildStateGroupsWithModdedStatesRepository(ObservableCollection<StateGroup> moddedStateGroups, Dictionary<string, List<string>> stateGroupsWithModdedStatesRepository);
        void BuildDialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository(Dictionary<string, List<string>> dialogueEventsWithStateGroups, Dictionary<string, Dictionary<string, string>> dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository);
        void ResetAudioProject();
    }

    public class AudioProjectService : IAudioProjectService
    {
        readonly ILogger _logger = Logging.Create<AudioEditorViewModel>();

        public AudioProjectData AudioProject { get; set; } = new AudioProjectData();
        public Dictionary<string, List<string>> StateGroupsWithModdedStatesRepository { get; set; } = [];
        public Dictionary<string, Dictionary<string, string>> DialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository { get; set; } = [];

        public void SaveAudioProject(PackFileService packFileService)
        {
            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            };

            var fileJson = JsonSerializer.Serialize(AudioProject, options);
            var pack = packFileService.GetEditablePack();
            var byteArray = Encoding.ASCII.GetBytes(fileJson);

            packFileService.AddFileToPack(pack, AudioProject.Directory, new PackFile($"{AudioProject.FileName}.aproj", new MemorySource(byteArray)));

            _logger.Here().Information($"Saved Audio Project file: {AudioProject.Directory}\\{AudioProject.FileName}.aproj");
        }

        public void LoadAudioProject(PackFileService packFileService, IAudioRepository audioRepository, AudioEditorViewModel audioEditorViewModel)
        {
            using var browser = new PackFileBrowserWindow(packFileService, [".aproj"]);

            if (browser.ShowDialog())
            {
                var filePath = browser.SelectedPath;
                var fileName = Path.GetFileName(filePath);
                var file = packFileService.FindFile(filePath);
                var bytes = file.DataSource.ReadData();
                var audioProjectJson = Encoding.UTF8.GetString(bytes);

                // Reset and initialise data.
                audioEditorViewModel.ResetAudioEditorViewModelData();
                ResetAudioProject();
                audioEditorViewModel.InitialiseCollections();

                // Set the AudioProject.
                AudioProject = JsonSerializer.Deserialize<AudioProjectData>(audioProjectJson);

                // Update AudioProjectTreeViewItems.
                AddAllSoundBanksToTreeViewItemsWrappers(this);

                // Get the Modded States and prepare them for being added to the DataGrid ComboBoxes.
                BuildStateGroupsWithModdedStatesRepository(AudioProject.ModdedStates, StateGroupsWithModdedStatesRepository);

                _logger.Here().Information($"Loaded Audio Project: {fileName}");

                if (audioEditorViewModel.AudioEditorVisibility == false)
                    audioEditorViewModel.SetAudioEditorVisibility(true);
            }
        }

        public void InitialiseAudioProject(AudioEditorViewModel audioEditorViewModel, string fileName, string directory, string language)
        {
            audioEditorViewModel.AudioProjectExplorerLabel = $"Audio Project Explorer - {AddExtraUnderscoresToString(fileName)}";

            AudioProject.FileName = fileName;
            AudioProject.Directory = directory;
            AudioProject.Language = language;

            InitialiseSoundBanks(AudioProject);

            InitialiseModdedStatesGroups(AudioProject.ModdedStates);

            AddAllDialogueEventsToSoundBankTreeViewItems(AudioProject, audioEditorViewModel.ShowEditedDialogueEventsOnly);

            SortSoundBanksAlphabetically(AudioProject.SoundBanks);

            AddAllSoundBanksToTreeViewItemsWrappers(this);
        }

        private static void InitialiseSoundBanks(AudioProjectData audioProject)
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

        private static void InitialiseModdedStatesGroups(ObservableCollection<StateGroup> moddedStates)
        {
            foreach (var moddedStateGroup in ModdedStateGroups)
            {
                var stateGroup = new StateGroup { Name = moddedStateGroup };
                moddedStates.Add(stateGroup);
            }
        }

        public void BuildStateGroupsWithModdedStatesRepository(ObservableCollection<StateGroup> moddedStateGroups, Dictionary<string, List<string>> stateGroupsWithModdedStatesRepository)
        {
            if (stateGroupsWithModdedStatesRepository == null)
                stateGroupsWithModdedStatesRepository = new Dictionary<string, List<string>>();
            else
                stateGroupsWithModdedStatesRepository.Clear();

            foreach (var stateGroup in moddedStateGroups)
            {
                if (stateGroup.States != null && stateGroup.States.Count > 0)
                {
                    foreach (var state in stateGroup.States)
                    {
                        if (!stateGroupsWithModdedStatesRepository.ContainsKey(stateGroup.Name))
                            stateGroupsWithModdedStatesRepository[stateGroup.Name] = new List<string>();

                        stateGroupsWithModdedStatesRepository[stateGroup.Name].Add(state.Name);
                    }
                }
            }
        }

        // Add qualifiers to State Groups so that dictionary keys are unique as some events have the same State Group twice e.g. VO_Actor.
        public void BuildDialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository(Dictionary<string, List<string>> dialogueEventsWithStateGroups, Dictionary<string, Dictionary<string, string>> dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository)
        {
            foreach (var dialogueEvent in dialogueEventsWithStateGroups)
            {
                var stateGroupsWithQualifiers = new Dictionary<string, string>();
                var stateGroups = dialogueEvent.Value;

                var voActorCount = 0;
                var voCultureCount = 0;

                foreach (var stateGroup in stateGroups)
                {
                    if (stateGroup == "VO_Actor")
                    {
                        voActorCount++;

                        var qualifier = voActorCount > 1 ? "VO_Actor (Reference)" : "VO_Actor (Source)";
                        stateGroupsWithQualifiers[qualifier] = "VO_Actor";
                    }
                    else if (stateGroup == "VO_Culture")
                    {
                        voCultureCount++;

                        var qualifier = voCultureCount > 1 ? "VO_Culture (Reference)" : "VO_Culture (Source)";
                        stateGroupsWithQualifiers[qualifier] = "VO_Culture";
                    }
                    else
                    {
                        // No qualifier needed, add the same state group as both original and qualified
                        stateGroupsWithQualifiers[stateGroup] = stateGroup;
                    }
                }

                dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository[dialogueEvent.Key] = stateGroupsWithQualifiers;
            }
        }

        public void ResetAudioProject()
        {
            AudioProject = new AudioProjectData();
        }
    }
}
