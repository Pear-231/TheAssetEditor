using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using static Editors.Audio.AudioEditor.AudioEditorHelpers;
using static Editors.Audio.AudioEditor.AudioProject;

namespace Editors.Audio.AudioEditor
{
    public class StatesProjectData
    {
        public class StatesAudioProject
        {
            public List<StatesProjectItems> StatesProjectItems { get; set; } = [];
        }

        public class StatesProjectItems
        {
            public List<StateGroupStatePair> StatesProjectItem { get; set; } = [];
        }

        public class StateGroupStatePair
        {
            public string StateGroup { get; set; }
            public string State { get; set; }
        }

        public static readonly List<string> ModdedStateGroups = ["VO_Actor", "VO_Culture", "VO_Faction_Leader", "VO_Battle_Selection", "VO_Battle_Special_Ability"];

        public static void ConvertDataGridToStatesAudioProject(ObservableCollection<Dictionary<string, object>> dataGridData)
        {
            var statesAudioProject = AudioProjectInstance.StatesAudioProject;
            statesAudioProject.StatesProjectItems = new List<StatesProjectItems>();

            var stateGroups = ModdedStateGroups;

            foreach (var dataGridItem in dataGridData)
            {
                var statesAudioProjectItem = new StatesProjectItems
                {
                    StatesProjectItem = new List<StateGroupStatePair>()
                };

                foreach (var stateGroup in stateGroups)
                {
                    var stateGroupKey = AddExtraUnderscoresToString(stateGroup); 
                    var state = dataGridItem.ContainsKey(stateGroupKey) ? dataGridItem[stateGroupKey].ToString() : string.Empty;

                    var stateGroupStatePair = new StateGroupStatePair
                    {
                        StateGroup = stateGroup,
                        State = state
                    };

                    statesAudioProjectItem.StatesProjectItem.Add(stateGroupStatePair);
                }

                statesAudioProject.StatesProjectItems.Add(statesAudioProjectItem);
            }
        }

        public static void ConvertStatesAudioProjectToDataGrid(ObservableCollection<Dictionary<string, object>> dataGridData, StatesAudioProject statesAudioProject)
        {
            foreach (var statesAudioProjectItem in statesAudioProject.StatesProjectItems)
            {
                var dataGridRow = new Dictionary<string, object>();

                foreach (var stateGroupStatePair in statesAudioProjectItem.StatesProjectItem)
                {
                    var stateGroup = AddExtraUnderscoresToString(stateGroupStatePair.StateGroup);
                    var state = stateGroupStatePair.State;

                    dataGridRow[stateGroup] = state;
                }

                dataGridData.Add(dataGridRow);
            }
        }
    }
}
