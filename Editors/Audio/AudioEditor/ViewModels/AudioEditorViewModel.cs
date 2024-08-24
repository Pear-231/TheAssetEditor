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

        // Controls for the user.
        [ObservableProperty] private string _selectedAudioProjectEvent;
        [ObservableProperty] private bool _showCustomStatesOnly;

        // AudioEditor data.
        [ObservableProperty] private ObservableCollection<string> _audioProjectEvents = [];
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _dataGridBuilderData = [];
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _dataGridData = [];

        public AudioEditorViewModel(IAudioRepository audioRepository, PackFileService packFileService, IWindowFactory windowFactory, SoundPlayer soundPlayer)
        {
            _audioRepository = audioRepository;
            _packFileService = packFileService;
            _windowFactory = windowFactory;
            _soundPlayer = soundPlayer;

            DataGridBuilderData.CollectionChanged += OnDataGridBuilderDataChanged;
        }

        private void OnDataGridBuilderDataChanged(object dataGridBuilderData, NotifyCollectionChangedEventArgs e)
        {
            // May use later...
        }

        partial void OnSelectedAudioProjectEventChanged(string oldValue, string newValue)
        {
            if ((oldValue == "" || oldValue == null) && (newValue == "" || newValue == null))
                return;

            AudioProjectInstance.SelectedAudioProjectEvent = newValue;
            AudioProjectInstance.PreviousSelectedAudioProjectEvent = oldValue;

            // Load the Event upon selection.
            if (AudioProjectInstance.Type == ProjectType.voaproj)
            {
                ConvertDataGridBuilderDataToVOProject(DataGridBuilderData, AudioProjectInstance.PreviousSelectedAudioProjectEvent); // Save the DataGridBuilderData from the Event that was just being worked (PreviousSelectedAudioProjectEvent) on to the Audio Project.

                LoadDialogueEvent(_audioRepository, ShowCustomStatesOnly);

                AddRowToDataGridBuilder();
            }

            else if (AudioProjectInstance.Type == ProjectType.statesaproj)
            {
                ConvertDataGridBuilderDataToStatesProject(DataGridBuilderData);
                
                LoadModdedStates(_audioRepository);
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

        [RelayCommand] public void LoadVOProject()
        {
            using var browser = new PackFileBrowserWindow(_packFileService, [ProjectType.voaproj.ToString()]);

            if (browser.ShowDialog())
            {
                // Remove any pre-existing data otherwise DataGrid isn't happy.
                ResetAudioEditorViewModelData();
                AudioProjectInstance.ResetAudioProjectData();

                // Create the object for State Groups with qualifiers so that their keys in the AudioProject dictionary are unique.
                AddQualifiersToStateGroups(_audioRepository.DialogueEventsWithStateGroups);

                var filePath = browser.SelectedPath;
                var file = _packFileService.FindFile(filePath);
                var bytes = file.DataSource.ReadData();
                var voProjectJson = Encoding.UTF8.GetString(bytes);
                var voProject = JsonSerializer.Deserialize<VOProject>(voProjectJson);

                // Set the data.
                AudioProjectInstance.VOProject = voProject;

                var fileName = AudioProjectInstance.FileName;
                _logger.Here().Information($"Loaded VO Audio Project file: {fileName}");

                // Create the list of Events used in the Events ComboBox.
                CreateEventsListFromVOProject();

                // Load the object which stores the custom States for use in the States ComboBox.
                //PrepareCustomStatesForComboBox(this);
            }
        }

        [RelayCommand] public void LoadStatesProject()
        {
            using var browser = new PackFileBrowserWindow(_packFileService, [ProjectType.statesaproj.ToString()]);

            if (browser.ShowDialog())
            {
                // Remove any pre-existing data otherwise DataGrid isn't happy.
                ResetAudioEditorViewModelData();
                AudioProjectInstance.ResetAudioProjectData();

                var filePath = browser.SelectedPath;
                var file = _packFileService.FindFile(filePath);
                var bytes = file.DataSource.ReadData();
                var statesProjectJson = Encoding.UTF8.GetString(bytes);
                var statesProject = JsonSerializer.Deserialize<StatesProject>(statesProjectJson);

                // Set the data.
                AudioProjectInstance.StatesProject = statesProject;

                var fileName = AudioProjectInstance.FileName;
                _logger.Here().Information($"Loaded States Audio Project file: {fileName}");

                // Create the list of Events used in the Events ComboBox.
                CreateEventsListFromStatesProject();
            }
        }

        [RelayCommand] public void SaveAudioProject()
        {
            if (AudioProjectInstance.Type == ProjectType.voaproj)
            {
                ConvertDataGridBuilderDataToVOProject(DataGridBuilderData, SelectedAudioProjectEvent);

                AddToPackFile(_packFileService, AudioProjectInstance.VOProject, AudioProjectInstance.FileName, AudioProjectInstance.Directory, AudioProjectInstance.Type);
            }

            else if (AudioProjectInstance.Type == ProjectType.statesaproj)
            {
                ConvertDataGridBuilderDataToStatesProject(DataGridBuilderData);

                AddToPackFile(_packFileService, AudioProjectInstance.StatesProject, AudioProjectInstance.FileName, AudioProjectInstance.Directory, AudioProjectInstance.Type);
            }
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
            DataGridBuilderData.Clear();
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

        public void LoadDialogueEvent(IAudioRepository audioRepository, bool showCustomStatesOnly)
        {
            if (string.IsNullOrEmpty(SelectedAudioProjectEvent))
                return;

            // Configure the DataGrids before adding the data to it so that once you add the data it populates.
            ConfigureDataGridBuilder(this, audioRepository, showCustomStatesOnly, "AudioEditorDataGridBuilder", DataGridBuilderData);
            ConfigureDataGrid(this, audioRepository, showCustomStatesOnly, "AudioEditorDataGrid", DataGridData);

            var voProject = AudioProjectInstance.VOProject;
            var dialogueEvent = voProject.DialogueEvents.FirstOrDefault(dialogueEvent => dialogueEvent.Name == SelectedAudioProjectEvent);
            var decisionTree = dialogueEvent.DecisionTree;

            DataGridBuilderData.Clear();

            /*
            if (decisionTree.Count > 0)
                ConvertVOProjectToDataGridBuilderData(DataGridBuilderData, voProject, SelectedAudioProjectEvent);

            else
            
                InitialiseDialogueEventDataGridBuilderData(decisionTree);
            */

            _logger.Here().Information($"Loaded Event: {SelectedAudioProjectEvent}");
        }

        public void LoadModdedStates(IAudioRepository audioRepository)
        {
            if (string.IsNullOrEmpty(SelectedAudioProjectEvent))
                return;

            // Configure the DataGrid before adding the data to it so that once you add the data it populates.
            ConfigureDataGrid(this, "AudioEditorDataGrid", DataGridBuilderData);

            var statesProject = AudioProjectInstance.StatesProject;
            var audioProjectItems = statesProject.StatesProjectItems;

            DataGridBuilderData.Clear();

            if (audioProjectItems.Count > 0)
                ConvertStatesProjectToDataGridBuilderData(DataGridBuilderData, statesProject);

            else
                InitialiseModdedStatesDataGridBuilderData();

            _logger.Here().Information($"Loaded Event: {SelectedAudioProjectEvent}");
        }

        private void InitialiseDialogueEventDataGridBuilderData(List<DecisionNode> decisionTree)
        {
            // Process Decision Tree and populate the objects in dataGridBuilderData. Make sure to add the kvp for the column header i.e. the stateGroupWithQualifier (with extra underscores) to prevent a binding error. 
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
                    foreach (var kvp in stateGroupsWithQualifiers)
                    {
                        var stateGroupWithQualifier = kvp.Key;
                        dataGridRow[AddExtraUnderscoresToString(stateGroupWithQualifier)] = string.Empty;
                    }
                }

                DataGridBuilderData.Add(dataGridRow);
            }
        }

        private void InitialiseModdedStatesDataGridBuilderData()
        {
            var dataGridRow = new Dictionary<string, object>{};

            var stateGroups = ModdedStateGroups;

            foreach (var stateGroup in stateGroups)
                dataGridRow[AddExtraUnderscoresToString(stateGroup)] = string.Empty;

            DataGridBuilderData.Add(dataGridRow);
        }

        public void CreateEventsListFromVOProject()
        {
            AudioProjectEvents.Clear();

            foreach (var dialogueEventItem in AudioProjectInstance.VOProject.DialogueEvents)
                AudioProjectEvents.Add(dialogueEventItem.Name);
        }

        public void CreateEventsListFromStatesProject()
        {
            AudioProjectEvents.Clear();

            AudioProjectEvents.Add("modded_states");
        }

        public void ResetAudioEditorViewModelData()
        {
            SelectedAudioProjectEvent = null;
            ShowCustomStatesOnly = false;
            AudioProjectEvents.Clear();
            DataGridBuilderData.Clear();
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
