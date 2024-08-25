using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CommonControls.PackFileBrowser;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Views;
using Editors.Audio.Storage;
using Editors.Audio.Utility;
using Microsoft.Win32;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.WindowHandling;
using static Editors.Audio.AudioEditor.AudioEditorHelpers;
using static Editors.Audio.AudioEditor.AudioProject;
using static Editors.Audio.AudioEditor.DialogueEventDataGrid;
using static Editors.Audio.AudioEditor.ModdedStatesDataGrid;
using static Editors.Audio.AudioEditor.StatesProjectData;
using static Editors.Audio.AudioEditor.VOProjectData;

// TODO:
// something to remove rows with empty data
// something to validate no state paths are the same
// something for the modded states so that the datagrid for building isn't active but the main datagrid is.
// something for modded states so that when the file is saved it sorts the file so that states are sorted so that there's no empty gaps in the row (unless there's nothing to add there)
// Add sorting of the audio project events list so that its in alphabetical order (or the same order as the events list checkboxes)
// Add sorting to the DataGrid so it displays them in alphabetical order, probably do this via a button.

namespace Editors.Audio.AudioEditor.ViewModels
{
    public partial class AudioEditorViewModel : ObservableObject, IEditorViewModel
    {
        private readonly IAudioRepository _audioRepository;
        private readonly PackFileService _packFileService;
        private readonly IWindowFactory _windowFactory;
        private readonly SoundPlayer _soundPlayer;
        readonly ILogger _logger = Logging.Create<AudioEditorViewModel>();

        public NotifyAttr<string> DisplayName { get; set; } = new NotifyAttr<string>("Audio Editor");

        [ObservableProperty] private string _selectedAudioProjectEvent;
        [ObservableProperty] private bool _showCustomStatesOnly;

        [ObservableProperty] private ObservableCollection<string> _audioProjectEvents = [];
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _dataGridBuilderData = [];
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _dataGridData = [];

        public AudioEditorViewModel(IAudioRepository audioRepository, PackFileService packFileService, IWindowFactory windowFactory, SoundPlayer soundPlayer)
        {
            _audioRepository = audioRepository;
            _packFileService = packFileService;
            _windowFactory = windowFactory;
            _soundPlayer = soundPlayer;

            DataGridData.CollectionChanged += OnDataGridDataChanged;
        }

        private void OnDataGridDataChanged(object dataGridData, NotifyCollectionChangedEventArgs e)
        {
            // May use later...
        }

        partial void OnSelectedAudioProjectEventChanged(string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(newValue))
                return;

            AudioProjectInstance.SelectedAudioProjectEvent = newValue;

            if (AudioProjectInstance.Type == ProjectType.voaproj)
            {
                var areStateGroupsEqual = false;

                if (!string.IsNullOrEmpty(oldValue))
                {
                    AudioProjectInstance.PreviousSelectedAudioProjectEvent = oldValue;

                    var newEventStateGroups = _audioRepository.DialogueEventsWithStateGroups[newValue];
                    var oldEventStateGroups = _audioRepository.DialogueEventsWithStateGroups[oldValue];
                    areStateGroupsEqual = newEventStateGroups.SequenceEqual(oldEventStateGroups);

                    // Save the old Event data to the Audio Project.
                    ConvertDataGridToVOAudioProject(DataGridData, AudioProjectInstance.PreviousSelectedAudioProjectEvent);
                }

                // Load the new Event data from the audio project
                LoadDialogueEvent(_audioRepository, ShowCustomStatesOnly, areStateGroupsEqual);
            }

            else if (AudioProjectInstance.Type == ProjectType.statesaproj)
            {
                // Save the Event data to the Audio Project.
                ConvertDataGridToStatesAudioProject(DataGridData);
                
                LoadModdedStates();
            }
        }

        partial void OnShowCustomStatesOnlyChanged(bool value)
        {
            // Load the Event again to reset the ComboBoxes in the DataGrid.
            LoadDialogueEvent(_audioRepository, ShowCustomStatesOnly);
        }

        [RelayCommand] public void NewVOProject()
        {
            var window = _windowFactory.Create<NewVOAudioProjectViewModel, NewVOAudioProjectView>("New VO Audio Project", 560, 530);
            window.AlwaysOnTop = false;
            window.ResizeMode = ResizeMode.NoResize;
            window.ShowWindow();
        }

        [RelayCommand] public void NewStatesProject()
        {
            var window = _windowFactory.Create<NewCustomStatesViewModel, NewCustomStatesView>("New States Audio Project", 560, 160);
            window.AlwaysOnTop = false;
            window.ResizeMode = ResizeMode.NoResize;
            window.ShowWindow();
        }

        [RelayCommand] public void SaveAudioProject()
        {
            if (AudioProjectInstance.Type == ProjectType.voaproj)
            {
                ConvertDataGridToVOAudioProject(DataGridData, SelectedAudioProjectEvent);

                AddToPackFile(_packFileService, AudioProjectInstance.VOAudioProject, AudioProjectInstance.FileName, AudioProjectInstance.Directory, AudioProjectInstance.Type);
            }

            else if (AudioProjectInstance.Type == ProjectType.statesaproj)
            {
                ConvertDataGridToStatesAudioProject(DataGridData);

                AddToPackFile(_packFileService, AudioProjectInstance.StatesAudioProject, AudioProjectInstance.FileName, AudioProjectInstance.Directory, AudioProjectInstance.Type);
            }
        }

        [RelayCommand] public void LoadVOProject()
        {
            var audioProjectType = ProjectType.voaproj;
            using var browser = new PackFileBrowserWindow(_packFileService, [ProjectType.voaproj.ToString()]);

            if (browser.ShowDialog())
            {
                // Remove any pre-existing data otherwise DataGrid isn't happy.
                ResetAudioEditorViewModelData();
                AudioProjectInstance.ResetAudioProjectData();

                // Create the object for State Groups with qualifiers so that their keys in the AudioProject dictionary are unique.
                AddQualifiersToStateGroups(_audioRepository.DialogueEventsWithStateGroups);

                var filePath = browser.SelectedPath;
                var fileName = Path.GetFileName(filePath);
                var file = _packFileService.FindFile(filePath);
                var bytes = file.DataSource.ReadData();
                var voProjectJson = Encoding.UTF8.GetString(bytes);
                var voProject = JsonSerializer.Deserialize<VOAudioProject>(voProjectJson);

                // Set the AudioProjectInstance data.
                AudioProjectInstance.VOAudioProject = voProject;
                AudioProjectInstance.Type = audioProjectType;
                AudioProjectInstance.FileName = fileName;

                // Create the list of Events used in the Events ComboBox.
                CreateEventsListFromVOProject();

                // Load the object which stores the custom States for use in the States ComboBox.
                //PrepareCustomStatesForComboBox(this);

                _logger.Here().Information($"Loaded VO Audio Project: {fileName}");
            }
        }

        [RelayCommand] public void LoadStatesProject()
        {
            var audioProjectType = ProjectType.statesaproj;
            using var browser = new PackFileBrowserWindow(_packFileService, [audioProjectType.ToString()]);

            if (browser.ShowDialog())
            {
                // Remove any pre-existing data otherwise DataGrid isn't happy.
                ResetAudioEditorViewModelData();
                AudioProjectInstance.ResetAudioProjectData();

                var filePath = browser.SelectedPath;
                var fileName = Path.GetFileName(filePath);
                var file = _packFileService.FindFile(filePath);
                var bytes = file.DataSource.ReadData();
                var statesProjectJson = Encoding.UTF8.GetString(bytes);
                var statesProject = JsonSerializer.Deserialize<StatesAudioProject>(statesProjectJson);

                // Set the AudioProjectInstance data.
                AudioProjectInstance.StatesAudioProject = statesProject;
                AudioProjectInstance.Type = audioProjectType;
                AudioProjectInstance.FileName = fileName;

                // Create the list of Events used in the Events ComboBox.
                CreateEventsListFromStatesProject();

                _logger.Here().Information($"Loaded States Audio Project: {fileName}");
            }
        }

        public void LoadDialogueEvent(IAudioRepository audioRepository, bool showCustomStatesOnly, bool areStateGroupsEqual = false)
        {
            if (string.IsNullOrEmpty(SelectedAudioProjectEvent))
                return;

            // Configure the DataGrids.
            if (showCustomStatesOnly == true || !areStateGroupsEqual)
            {
                ConfigureDataGridBuilder(this, audioRepository, showCustomStatesOnly, "AudioEditorDataGridBuilder", DataGridBuilderData);
                ConfigureDataGrid(this, audioRepository, "AudioEditorDataGrid", DataGridData);
            }

            // Clear the previous DataGrid Data.
            ClearDataGridBuilderData(DataGridBuilderData);
            ClearDataGridData(DataGridData);

            // Populate the DataGrid with the data from the Audio Project.
            var vOAudioProject = AudioProjectInstance.VOAudioProject;
            var audioProjectItems = vOAudioProject.DialogueEvents;
            var dialogueEvent = audioProjectItems.FirstOrDefault(dialogueEvent => dialogueEvent.Name == SelectedAudioProjectEvent);
            var decisionTree = dialogueEvent.DecisionTree;

            if (audioProjectItems.Count > 0)
                ConvertVOAudioProjectToDataGrid(DataGridData, vOAudioProject, SelectedAudioProjectEvent);

            else
                InitialiseDialogueEventDataGrid(decisionTree);

            AddRowToDataGridBuilder();

            _logger.Here().Information($"Loaded Event: {SelectedAudioProjectEvent}");
        }

        public void LoadModdedStates()
        {
            if (string.IsNullOrEmpty(SelectedAudioProjectEvent))
                return;

            // Configure the DataGrids.
            ConfigureDataGrid(this, "AudioEditorDataGrid", DataGridBuilderData);
            ClearDataGridBuilderData(DataGridBuilderData);

            // Populate the DataGrid with the data from the Audio Project.
            var statesAudioProject = AudioProjectInstance.StatesAudioProject;
            var statesAudioProjectItems = statesAudioProject.StatesProjectItems;

            if (statesAudioProjectItems.Count > 0)
                ConvertStatesAudioProjectToDataGrid(DataGridBuilderData, statesAudioProject);

            else
                InitialiseModdedStatesDataGrid();

            _logger.Here().Information($"Loaded Event: {SelectedAudioProjectEvent}");
        }

        private void InitialiseDialogueEventDataGrid(List<DecisionNode> decisionTree)
        {
            // Process Decision Tree and populate the objects in the dataGrid.
            foreach (var statePath in decisionTree)
            {
                var filePaths = statePath.AudioFiles;
                var fileNames = filePaths.Select(Path.GetFileName);
                var fileNamesString = string.Join(", ", fileNames);

                var dataGridRow = new Dictionary<string, object>
                {
                    ["AudioFiles"] = new List<string>(statePath.AudioFiles), // Create a new instance of the list to avoid referencing the original collection.
                    ["AudioFilesDisplay"] = fileNamesString
                };

                if (DialogueEventsWithStateGroupsWithQualifiers.TryGetValue(SelectedAudioProjectEvent, out var stateGroupsWithQualifiers))
                {
                    // Make sure to add the kvp for the column header i.e. the stateGroupWithQualifier (with extra underscores) to prevent a binding error. 
                    foreach (var kvp in stateGroupsWithQualifiers)
                    {
                        var stateGroupWithQualifier = kvp.Key;
                        dataGridRow[AddExtraUnderscoresToString(stateGroupWithQualifier)] = string.Empty;
                    }
                }

                DataGridData.Add(dataGridRow);
            }
        }

        private void InitialiseModdedStatesDataGrid()
        {
            var dataGridRow = new Dictionary<string, object>{};

            var stateGroups = ModdedStateGroups;

            foreach (var stateGroup in stateGroups)
                dataGridRow[AddExtraUnderscoresToString(stateGroup)] = string.Empty;

            DataGridData.Add(dataGridRow);
        }

        public void CreateEventsListFromVOProject()
        {
            ClearAudioProjectEvents(AudioProjectEvents);

            foreach (var dialogueEventItem in AudioProjectInstance.VOAudioProject.DialogueEvents)
                AudioProjectEvents.Add(dialogueEventItem.Name);
        }

        public void CreateEventsListFromStatesProject()
        {
            ClearAudioProjectEvents(AudioProjectEvents);

            AudioProjectEvents.Add("modded_states");
        }

        public void AddRowToDataGridBuilder()
        {
            if (string.IsNullOrEmpty(SelectedAudioProjectEvent))
                return;

            var newRow = new Dictionary<string, object>();

            if (AudioProjectInstance.Type == ProjectType.voaproj)
            {
                var stateGroupsWithQualifiers = DialogueEventsWithStateGroupsWithQualifiers[SelectedAudioProjectEvent];

                foreach (var kvp in stateGroupsWithQualifiers)
                {
                    var stateGroupWithQualifier = kvp.Key;
                    var stateGroupKey = AddExtraUnderscoresToString(stateGroupWithQualifier);
                    newRow[stateGroupKey] = "";
                }

                newRow["AudioFiles"] = new List<string> { };
                newRow["AudioFilesDisplay"] = string.Empty;

                DataGridBuilderData.Add(newRow);
            }

            else if (AudioProjectInstance.Type == ProjectType.statesaproj)
            {
                var stateGroups = ModdedStateGroups;

                foreach (var stateGroup in stateGroups)
                {
                    var stateGroupKey = AddExtraUnderscoresToString(stateGroup);
                    newRow[stateGroupKey] = "";
                }

                DataGridBuilderData.Add(newRow);
            }
        }

        [RelayCommand] public void AddRowToDataGrid()
        {
            if (DataGridBuilderData.Count == 0)
                return;

            var newRow = new Dictionary<string, object>();

            foreach (var kvp in DataGridBuilderData[0])
            {
                var column = kvp.Key;
                var cellValue = kvp.Value;

                if (column == "AudioFiles" && cellValue is List<string> stringList)
                {
                    var newList = new List<string>(stringList);
                    newRow[column] = newList;
                }

                else
                    newRow[column] = cellValue.ToString();
            }

            DataGridData.Add(newRow);
            ClearDataGridBuilderData(DataGridBuilderData);
            AddRowToDataGridBuilder();
        }







        /*
            factory.SetValue(ContentControl.ContentProperty, "Play Audio");
            factory.SetValue(FrameworkElement.ToolTipProperty, "Play an audio file at random to simulate the Dialogue Event being triggered in game.");
        */










        public void RemoveStatePath(Dictionary<string, object> rowToRemove)
        {
            DataGridBuilderData.Remove(rowToRemove);
        }

        public static void AddAudioFiles(Dictionary<string, object> dataGridRow, TextBox textBox)
        {
            var dialog = new OpenFileDialog()
            {
                Multiselect = true,
                Filter = "WAV files (*.wav)|*.wav"
            };

            if (dialog.ShowDialog() == true)
            {
                var filePaths = dialog.FileNames;
                var fileNames = filePaths.Select(Path.GetFileName);
                var fileNamesString = string.Join(", ", fileNames);
                var filePathsString = string.Join(", ", filePaths.Select(filePath => $"\"{filePath}\""));

                textBox.Text = fileNamesString;
                textBox.ToolTip = filePathsString;

                var audioFiles = new List<string>(filePaths);

                dataGridRow["AudioFiles"] = audioFiles;
                dataGridRow["AudioFilesDisplay"] = fileNamesString;
            }
        }

        public void ResetAudioEditorViewModelData()
        {
            SelectedAudioProjectEvent = null;
            ShowCustomStatesOnly = false;
            ClearAudioProjectEvents(AudioProjectEvents);
            ClearDataGridBuilderData(DataGridBuilderData);
        }

        public void Close()
        {
            ResetAudioEditorViewModelData();
        }

        public bool Save() => true;

        public PackFile MainFile { get; set; }

        public bool HasUnsavedChanges { get; set; } = false;
    }
}
