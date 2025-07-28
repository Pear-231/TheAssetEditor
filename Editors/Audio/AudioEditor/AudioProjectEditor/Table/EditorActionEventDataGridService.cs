﻿using System.Collections.Generic;
using System.Data;
using Editors.Audio.AudioEditor.AudioProjectExplorer;
using Editors.Audio.AudioEditor.Presentation.Table;
using Editors.Audio.GameSettings.Warhammer3;
using Shared.Core.Events;
using Editors.Audio.AudioEditor.Events;

namespace Editors.Audio.AudioEditor.AudioProjectEditor.Table
{
    public class EditorActionEventDataGridService(
        IUiCommandFactory uiCommandFactory,
        IEventHub eventHub,
        IAudioEditorService audioEditorService) : IEditorTableService
    {
        private readonly IUiCommandFactory _uiCommandFactory = uiCommandFactory;
        private readonly IEventHub _eventHub = eventHub;
        private readonly IAudioEditorService _audioEditorService = audioEditorService;

        public AudioProjectExplorerTreeNodeType NodeType => AudioProjectExplorerTreeNodeType.ActionEventSoundBank;

        public void Load(DataTable table)
        {
            var schema = DefineSchema();
            ConfigureTable(schema);
            ConfigureDataGrid(schema);
            InitialiseTable(table);
        }

        public List<string> DefineSchema()
        {
            var schema = new List<string>();
            var columnName = TableInfo.EventColumnName;
            schema.Add(columnName);
            return schema;
        }

        public void ConfigureTable(List<string> schema)
        {
            foreach (var columnName in schema)
            {
                var column = new DataColumn(columnName, typeof(string));
                _eventHub.Publish(new EditorTableColumnAddedEvent(column));
            }
        }

        public void ConfigureDataGrid(List<string> schema)
        {
            var columnsCount = 1;
            var columnWidth = 1.0 / columnsCount;

            var selectedAudioProjectExplorerNode = _audioEditorService.SelectedAudioProjectExplorerNode;
            if (selectedAudioProjectExplorerNode.Name == SoundBanks.MoviesDisplayString)
            {
                var fileSelectColumnHeader = TableInfo.BrowseMovieColumnName;
                var fileSelectColumn = DataGridTemplates.CreateColumnTemplate(fileSelectColumnHeader, 85, useAbsoluteWidth: true);
                fileSelectColumn.CellTemplate = DataGridTemplates.CreateFileSelectButtonCellTemplate(_uiCommandFactory);
                _eventHub.Publish(new EditorDataGridColumnAddedEvent(fileSelectColumn));

                foreach (var columnName in schema)
                {
                    var eventColumn = DataGridTemplates.CreateColumnTemplate(columnName, columnWidth, isReadOnly: true);
                    eventColumn.CellTemplate = DataGridTemplates.CreateReadOnlyTextBlockTemplate(columnName);
                    _eventHub.Publish(new EditorDataGridColumnAddedEvent(eventColumn));
                }
            }
            else
            {
                foreach (var columnName in schema)
                {
                    var eventColumn = DataGridTemplates.CreateColumnTemplate(columnName, columnWidth, isReadOnly: true);
                    eventColumn.CellTemplate = DataGridTemplates.CreateEditableEventTextBoxTemplate(_eventHub, columnName);
                    _eventHub.Publish(new EditorDataGridColumnAddedEvent(eventColumn));
                }
            }
        }

        public void InitialiseTable(DataTable editorTable)
        {
            var eventName = string.Empty;

            var selectedAudioProjectExplorerNode = _audioEditorService.SelectedAudioProjectExplorerNode.Name;
            if (selectedAudioProjectExplorerNode != SoundBanks.MoviesDisplayString)
                eventName = "Play_";

            var row = editorTable.NewRow();
            row[TableInfo.EventColumnName] = eventName;

            _eventHub.Publish(new EditorTableRowAddedEvent(row));
        }
    }
}
