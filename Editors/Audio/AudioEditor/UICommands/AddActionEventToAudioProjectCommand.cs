using System.Collections.Generic;
using System.Data;
using Editors.Audio.AudioEditor.AudioProjectExplorer;
using Editors.Audio.AudioEditor.Models;

namespace Editors.Audio.AudioEditor.UICommands
{
    public class AddActionEventToAudioProjectCommand : IAudioProjectUICommand
    {
        private readonly IAudioEditorService _audioEditorService;

        public AudioProjectCommandAction Action => AudioProjectCommandAction.AddToAudioProject;
        public NodeType NodeType => NodeType.ActionEventSoundBank;

        public AddActionEventToAudioProjectCommand(IAudioEditorService audioEditorService)
        {
            _audioEditorService = audioEditorService;
        }

        public void Execute(DataRow row)
        {
            var audioFiles = _audioEditorService.AudioFiles;
            var audioSettings = _audioEditorService.AudioSettings;
            var actionEvent = new ActionEvent();
            var actionEventName = AudioProjectHelpers.GetActionEventNameFromRow(row);


            if (audioFiles.Count == 1)
            {
                var fileName = audioFiles[0].FileName;
                var filePath = audioFiles[0].FilePath;
                var soundSettings = SoundAudioSettings.CreateSoundSettings(audioSettings);
                var sound = Sound.Create(fileName, filePath, soundSettings);

                actionEvent = ActionEvent.Create(actionEventName, sound);
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

                var randomSequenceContainerSettings = RandomSequenceContainerSettings.CreateRandomSequenceContainerSettings(audioSettings);
                var randomSequenceContainerName = AudioProjectHelpers.GetActionEventNameFromRow(row); // TODO PUT SOME COMMENT WHY WE USE ACTIONEVENTNAME FOR RAND CONTAINER NAME
                var randomSequenceContainer = RandomSequenceContainer.Create(randomSequenceContainerName, randomSequenceContainerSettings, sounds);

                actionEvent = ActionEvent.Create(actionEventName, randomSequenceContainer);
            }

            var soundBankName = _audioEditorService.SelectedExplorerNode.Name;
            var soundBank = _audioEditorService.AudioProject.GetSoundBank(soundBankName);

            soundBank.InsertAlphabetically(actionEvent);
        }
    }
}
