using System.Collections.Generic;
using System.Collections.ObjectModel;
using Editors.Audio.AudioEditor.ViewModels;
using Editors.Audio.Storage;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace Editors.Audio.AudioEditor.AudioProject
{
    public interface IAudioProjectService
    {
        AudioProjectData AudioProject { get; set; }
        Dictionary<string, List<string>> StateGroupsWithModdedStatesRepository { get; set; }
        Dictionary<string, Dictionary<string, string>> DialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository { get; set; }
        Dictionary<string, List<string>> DialogueEventsWithStateGroupsWithIntegrityError { get; set; }
        void SaveAudioProject(IPackFileService packFileService);
        void LoadAudioProject(IPackFileService packFileService, IAudioRepository audioRepository, AudioEditorViewModel audioEditorViewModel, IStandardDialogs packFileUiProvider);
        void InitialiseAudioProject(AudioEditorViewModel audioEditorViewModel, string fileName, string directory, string language);
        void BuildStateGroupsWithModdedStatesRepository(ObservableCollection<StateGroup> moddedStateGroups, Dictionary<string, List<string>> stateGroupsWithModdedStatesRepository);
        void BuildDialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository(Dictionary<string, List<string>> dialogueEventsWithStateGroups, Dictionary<string, Dictionary<string, string>> dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository);
        void ResetAudioProject();
        string AudioProjectFileName { get; set; }
        string AudioProjectDirectory { get; set; }
    }
}
