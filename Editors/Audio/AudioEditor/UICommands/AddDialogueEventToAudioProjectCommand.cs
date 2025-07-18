using System.Collections.Generic;
using System.Data;
using Editors.Audio.AudioEditor.AudioProjectExplorer;
using Editors.Audio.AudioEditor.DataGrids;
using Editors.Audio.AudioEditor.Models;
using Editors.Audio.Storage;

namespace Editors.Audio.AudioEditor.UICommands
{
    public class AddDialogueEventToAudioProjectCommand : IAudioProjectUICommand
    {
        private readonly IAudioEditorService _audioEditorService;  
        private readonly IAudioRepository _audioRepository;

        public AudioProjectCommandAction Action => AudioProjectCommandAction.AddToAudioProject;
        public NodeType NodeType => NodeType.DialogueEvent;

        public AddDialogueEventToAudioProjectCommand(IAudioEditorService audioEditorService, IAudioRepository audioRepository)
        {
            _audioEditorService = audioEditorService;
            _audioRepository = audioRepository;
        }

        public void Execute(DataRow row)
        {
            var audioFiles = _audioEditorService.AudioFiles;
            var audioSettings = _audioEditorService.AudioSettings;

            var statePath = new StatePath();
            var dialogueEventName = _audioEditorService.SelectedExplorerNode.Name;
            var dialogueEvent = _audioEditorService.AudioProject.GetDialogueEvent(dialogueEventName);

            var statePathNodes = new List<StatePathNode>();
            var stateGroupsWithQualifiers = _audioRepository.QualifiedStateGroupLookupByStateGroupByDialogueEvent[dialogueEvent.Name];
            foreach (var stateGroupWithQualifier in stateGroupsWithQualifiers)
            {
                var stateGroupName = AudioProjectHelpers.GetStateGroupFromStateGroupWithQualifier(_audioRepository, dialogueEvent.Name, stateGroupWithQualifier.Value);
                var stateGroup = StateGroup.Create(stateGroupName);

                var stateGroupNameWithDoubledUnderscores = DataGridHelpers.DuplicateUnderscores(stateGroupName);
                var stateName = AudioProjectHelpers.GetValueFromRow(row, stateGroupNameWithDoubledUnderscores);
                var state = State.Create(stateName);

                var statePathNode = StatePathNode.Create(stateGroup, state);
                statePathNodes.Add(statePathNode);
            }

            if (audioFiles.Count == 1)
            {
                var fileName = audioFiles[0].FileName;
                var filePath = audioFiles[0].FilePath;
                var soundSettings = AudioSettings.CreateSoundSettings(audioSettings);
                var sound = Sound.Create(fileName, filePath, soundSettings);

                statePath = StatePath.Create(statePathNodes, sound);
            }
            else if (audioFiles.Count > 1)
            {
                var sounds = new List<Sound>();

                foreach (var audioFile in audioFiles)
                {
                    var fileName = audioFile.FileName;
                    var filePath = audioFile.FilePath;
                    var sound = Sound.Create(fileName, filePath);
                    sounds.Add(sound);
                }

                var randomSequenceContainerSettings = AudioSettings.CreateRandomSequenceContainerSettings(audioSettings);
                var randomSequenceContainerName = AudioProjectHelpers.GetActionEventNameFromRow(row);
                var randomSequenceContainer = RandomSequenceContainer.Create(randomSequenceContainerName, randomSequenceContainerSettings, sounds);

                statePath = StatePath.Create(statePathNodes, randomSequenceContainer);
            }

            dialogueEvent.InsertAlphabetically(statePath);
        }
    }
}
