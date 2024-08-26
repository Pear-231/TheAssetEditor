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
using static Editors.Audio.AudioEditor.DataGridConfiguration;
using static Editors.Audio.AudioEditor.StatesProjectData;
using static Editors.Audio.AudioEditor.VOProjectData;
using static Editors.Audio.Utility.SoundPlayer;

// TODO:
// something to remove rows with empty data
// something to validate no state paths are the same
// something for modded states so that when the file is saved it sorts the file so that states are sorted so that there's no empty gaps in the row (unless there's nothing to add there)
// Add sorting of the audio project events list so that its in alphabetical order (or the same order as the events list checkboxes)
// Add sorting to the DataGrid so it displays them in alphabetical order, probably do this via a button.
// Make some way of turning dialogue events into audio projects
// Add some sorting of to the modded states files.

// Features:
// Copy and paste of rows - need to ensure that you can only copy or paste, not both though you need to manage cases where this you open an event to paste into it and you instead want to copy
// Audio compiler update
// Fix the combobox delays or just make it stop highlighting the text when you click it
// Need visibility stuff for the menu so that you can't paste between events unless it's a dialogue event

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

        // Audio Editor data.
        [ObservableProperty] private ObservableCollection<string> _audioProjectEvents = [];
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _dataGridBuilderData = [];
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _dataGridData = [];
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _selectedDataGridRows = [];
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _copiedDataGridRows = [];

        // Properties the user can control.
        [ObservableProperty] private string _selectedAudioProjectEvent;
        [ObservableProperty] private bool _showModdedStatesOnly;

        // UI visibility controls.
        [ObservableProperty] private bool _audioEditorVisibility = false;
        [ObservableProperty] private bool _audioProjectExplorerVisibility = false;        
        [ObservableProperty] private bool _dataGridBuilderAndControlsVisibility = false;
        [ObservableProperty] private bool _dataGridControlsVisibility = false;
        [ObservableProperty] private bool _dataGridVisibility = false;
        [ObservableProperty] private bool _dataGridContextMenuVisibility = false;

        // UI enablement controls.
        [ObservableProperty] private bool _isPlayAudioButtonEnabled = false;
        [ObservableProperty] private bool _isPasteEnabled = true;

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
            SetIsPasteEnabled();
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
                LoadDialogueEvent(ShowModdedStatesOnly, areStateGroupsEqual);
            }

            SetIsPasteEnabled();
        }

        partial void OnShowModdedStatesOnlyChanged(bool value)
        {
            ConfigureAudioProjectDataGridBuilder(this, _audioRepository, ShowModdedStatesOnly, "AudioEditorDataGridBuilder", DataGridBuilderData);
            
            ClearDataGridBuilderData(DataGridBuilderData);
            
            AddRowToDataGridBuilder();
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
            using var browser = new PackFileBrowserWindow(_packFileService, [audioProjectType.ToString()]);

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
                var vOAudioProjectJson = Encoding.UTF8.GetString(bytes);
                var vOAudioProject = JsonSerializer.Deserialize<VOAudioProject>(vOAudioProjectJson);

                // Set the AudioProjectInstance data.
                AudioProjectInstance.VOAudioProject = vOAudioProject;
                AudioProjectInstance.Type = audioProjectType;
                AudioProjectInstance.FileName = fileName;

                // Create the list of Events used in the Events ComboBox.
                CreateEventsListFromVOProject();

                // Get the modded states from States Audio Project and prepare them for being added to the DataGrid ComboBoxes.
                PrepareModdedStatesForComboBox(_packFileService);

                _logger.Here().Information($"Loaded VO Audio Project: {fileName}");
            }
        }

        [RelayCommand] public void LoadStatesProject()
        {
            var audioProjectType = ProjectType.statesaproj;
            using var browser = new PackFileBrowserWindow(_packFileService, [audioProjectType.ToString()]);

            if (browser.ShowDialog())
            {
                // Remove any pre-existing data otherwise Data_packFileServiceGrid isn't happy.
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

        public void LoadDialogueEvent(bool showModdedStatesOnly, bool areStateGroupsEqual = false)
        {
            if (string.IsNullOrEmpty(SelectedAudioProjectEvent))
                return;

            // Configure the DataGrids.
            if (showModdedStatesOnly == true || areStateGroupsEqual == false)
            {
                ConfigureAudioProjectDataGridBuilder(this, _audioRepository, showModdedStatesOnly, "AudioEditorDataGridBuilder", DataGridBuilderData);
                ConfigureAudioProjectDataGrid(this, _audioRepository, "AudioEditorDataGrid", DataGridData);
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
            // Configure the DataGrid.
            ConfigureStatesAudioProjectDataGrid(this, "AudioEditorDataGrid", DataGridData);
            ClearDataGridData(DataGridData);

            // Populate the DataGrid with the data from the Audio Project.
            var statesAudioProject = AudioProjectInstance.StatesAudioProject;
            var statesAudioProjectItems = statesAudioProject.StatesProjectItems;

            if (statesAudioProjectItems.Count > 0)
                ConvertStatesAudioProjectToDataGrid(DataGridBuilderData, statesAudioProject);

            else
                InitialiseModdedStatesDataGrid();
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
                    var columnHeader = AddExtraUnderscoresToString(stateGroupWithQualifier);
                    newRow[columnHeader] = "";
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

        [RelayCommand] public void AddRowFromDataGridBuilderToDataGrid()
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

        [RelayCommand] public void AddRowToDataGrid()
        {
            var newRow = new Dictionary<string, object>();
            DataGridData.Add(newRow);
        }

        [RelayCommand] public void PlayRandomAudioFile()
        {
            if (SelectedDataGridRows.Count == 1)
            {
                if (SelectedDataGridRows[0].TryGetValue("AudioFiles", out var audioFilesObj) && audioFilesObj is List<string> audioFiles && audioFiles.Any())
                {
                    var random = new Random();
                    var randomIndex = random.Next(audioFiles.Count);
                    var randomAudioFile = audioFiles[randomIndex];

                    PlaySound(randomAudioFile);
                }
            }
        }

        [RelayCommand] public void CopyRows()
        {
            // Initialise new data rather than setting it directly so it's not referencing the original object which for example gets cleared when a new Event is selected.
            CopiedDataGridRows = new ObservableCollection<Dictionary<string, object>>();

            foreach (var item in SelectedDataGridRows)
                CopiedDataGridRows.Add(new Dictionary<string, object>(item));

            SetIsPasteEnabled();
        }

        [RelayCommand] public void PasteRows()
        {
            if (IsPasteEnabled)
            {
                foreach (var copiedDataGridRow in CopiedDataGridRows)
                    DataGridData.Add(copiedDataGridRow);
            }

            SetIsPasteEnabled();
        }

        public void SetIsPasteEnabled()
        {
            if (CopiedDataGridRows.Count == 0)
                return;

            var areAnyCopiedRowsInDataGrid = CopiedDataGridRows.Any(copiedRow => DataGridData.Any(dataGridRow => copiedRow.Count == dataGridRow.Count && !copiedRow.Except(dataGridRow).Any()));

            var stateGroupsWithQualifiers = DialogueEventsWithStateGroupsWithQualifiers[SelectedAudioProjectEvent];

            var dialogueEventStateGroups = new List<string>();

            foreach (var kvp in stateGroupsWithQualifiers)
            {
                var stateGroupWithQualifier = AddExtraUnderscoresToString(kvp.Key);
                dialogueEventStateGroups.Add(stateGroupWithQualifier);
            }

            var copiedDataGridRowStateGroups = new List<string>();

            foreach (var kvp in CopiedDataGridRows[0])
            {
                if (kvp.Key != "AudioFiles" && kvp.Key != "AudioFilesDisplay")
                    copiedDataGridRowStateGroups.Add(kvp.Key);
            }

            var areStateGroupsEqual = dialogueEventStateGroups.SequenceEqual(copiedDataGridRowStateGroups);

            if (!areStateGroupsEqual || areAnyCopiedRowsInDataGrid)
                IsPasteEnabled = false;

            else if (areStateGroupsEqual && !areAnyCopiedRowsInDataGrid)
                IsPasteEnabled = true;
        }


        public void RemoveRowFromDataGrid(Dictionary<string, object> rowToRemove)
        {
            DataGridData.Remove(rowToRemove);
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

        public void SetAudioEditorVisibility(bool isVisible)
        {
            AudioEditorVisibility = isVisible;
        }

        public void SetAudioProjectExplorerVisibility(bool isVisible)
        {
            AudioProjectExplorerVisibility = isVisible;
        }

        public void SetDataGridBuilderAndControlsVisibility(bool isVisible)
        {
            DataGridBuilderAndControlsVisibility = isVisible;
        }

        public void SetDataGridControlsVisibility(bool isVisible)
        {
            DataGridControlsVisibility = isVisible;
        }

        public void SetDataGridVisibility(bool isVisible)
        {
            DataGridVisibility = isVisible;
        }

        public void SetDataGridContextMenuVisibility(bool isVisible)
        {
            DataGridContextMenuVisibility = isVisible;
        }

        public void ResetAudioEditorViewModelData()
        {
            SelectedAudioProjectEvent = null;
            ShowModdedStatesOnly = false;
            ClearAudioProjectEvents(AudioProjectEvents);
            ClearDataGridData(DataGridData);
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
