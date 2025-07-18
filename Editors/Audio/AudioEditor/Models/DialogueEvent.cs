using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Editors.Audio.AudioEditor.DataGrids;
using Editors.Audio.Storage;
using Shared.GameFormats.Wwise.Enums;

namespace Editors.Audio.AudioEditor.Models
{
    public class DialogueEvent : AudioProjectHircItem
    {
        public override AkBkHircType HircType { get; set; } = AkBkHircType.Dialogue_Event;
        public List<StatePath> StatePaths { get; set; }

        // TODO: Figure out how this function actually is used cause I can't remember and it's not clear!   
        public StatePath GetValidStatePath(IAudioRepository audioRepository, DataRow row)
        {
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
                var statePathNode = new StatePathNode
                {
                    StateGroup = new StateGroup { Name = AudioProjectHelpers.GetStateGroupFromStateGroupWithQualifier(audioRepository, Name, stateGroupColumnName) },
                    State = new State { Name = stateName }
                };
                rowStatePathNodes.Add(statePathNode);
            }

            foreach (var statePath in StatePaths)
            {
                if (statePath.Nodes.SequenceEqual(rowStatePathNodes))
                    return statePath;
            }

            return null;
        }

        public void InsertAlphabetically(StatePath statePath) => InsertAlphabeticallyUnique(StatePaths, statePath);
    }
}
