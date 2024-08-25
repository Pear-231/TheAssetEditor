using System.Collections.Generic;
using static Editors.Audio.AudioEditor.StatesProjectData;
using static Editors.Audio.AudioEditor.VOProjectData;

namespace Editors.Audio.AudioEditor
{
    public class AudioProject
    {
        public enum ProjectType
        {
            sfxaproj,
            voaproj,
            statesaproj
        }

        private static readonly AudioProject _instance = new();

        public static AudioProject AudioProjectInstance => _instance;

        public VOAudioProject VOAudioProject { get; set; } = new VOAudioProject();

        public StatesAudioProject StatesAudioProject { get; set; } = new StatesAudioProject();

        public static Dictionary<string, Dictionary<string, string>> DialogueEventsWithStateGroupsWithQualifiers { get; set; } = new();

        public Dictionary<string, List<string>> StateGroupsWithCustomStates { get; set; } = new();

        public ProjectType? Type { get; set; }

        public string FileName { get; set; }

        public string Directory { get; set; }

        public string SelectedAudioProjectEvent { get; set; }

        public string PreviousSelectedAudioProjectEvent { get; set; }

        public void ResetAudioProjectData()
        {
            VOAudioProject = null;
            StatesAudioProject = null;
            DialogueEventsWithStateGroupsWithQualifiers = null;
            StateGroupsWithCustomStates = null;
            Type = null;
            FileName = null;
            Directory = null;
            SelectedAudioProjectEvent = null;
            PreviousSelectedAudioProjectEvent = null;
        }
    }
}
