using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Settings.Warhammer3;
using Editors.Audio.AudioEditor.Views;
using Editors.Audio.Storage;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using static Editors.Audio.AudioEditor.AudioEditorHelpers;
using static Editors.Audio.AudioEditor.AudioProjectOrganiser;
using static Editors.Audio.AudioEditor.DataGridCopyPasteHandler;
using static Editors.Audio.AudioEditor.DataGridHandler;
using static Editors.Audio.AudioEditor.DialogueEventFilter;
using static Editors.Audio.AudioEditor.Settings.Warhammer3.DialogueEvents;
using static Editors.Audio.AudioEditor.Settings.Warhammer3.SoundBanks;
using static Editors.Audio.AudioEditor.TreeViewItemLoader;
using static Editors.Audio.AudioEditor.TreeViewWrapper;
using static Editors.Audio.Utility.SoundPlayer;

namespace Editors.Audio.AudioEditor.ViewModels
{
    public partial class AudioEditorViewModel : ObservableObject, IEditorViewModel
    {
        private readonly IAudioRepository _audioRepository;
        private readonly PackFileService _packFileService;
        private readonly IAudioProjectService _audioProjectService;
        readonly ILogger _logger = Logging.Create<AudioEditorViewModel>();

        public NotifyAttr<string> DisplayName { get; set; } = new NotifyAttr<string>("Audio Editor");

        // Audio Editor data.
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _audioProjectEditorSingleRowDataGrid;
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _audioProjectEditorFullDataGrid;
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _selectedDataGridRows;
        [ObservableProperty] private ObservableCollection<Dictionary<string, object>> _copiedDataGridRows;
        [ObservableProperty] public ObservableCollection<SoundBank> _soundBanks;
        [ObservableProperty] public ObservableCollection<object> _audioProjectTreeViewItems;

        // Audio Project Explorer.
        [ObservableProperty] private string _audioProjectExplorerLabel = "Audio Project Explorer";
        public object _selectedAudioProjectTreeItem;
        public object _previousSelectedAudioProjectTreeItem;
        [ObservableProperty] private string _selectedDialogueEventPreset;
        [ObservableProperty] private bool _showEditedSoundBanksOnly;
        [ObservableProperty] private bool _showEditedDialogueEventsOnly;
        [ObservableProperty] private bool _isDialogueEventPresetFilterEnabled = false;
        [ObservableProperty] private ObservableCollection<SoundBanks.SoundBank> _dialogueEventSoundBanks = new(Enum.GetValues<SoundBanks.SoundBank>().Where(soundBank => GetSoundBankType(soundBank) == SoundBankType.DialogueEventSoundBank));
        [ObservableProperty] private ObservableCollection<string> _dialogueEventPresets;
        public Dictionary<string, string> DialogueEventSoundBankFiltering { get; set; } = [];

        // Audio Project Editor.
        [ObservableProperty] private string _audioProjectEditorLabel = "Audio Project Editor";
        [ObservableProperty] private string _audioProjectEditorSingleRowDataGridTag = "AudioProjectEditorSingleRowDataGrid";
        [ObservableProperty] private string _audioProjectEditorFullDataGridTag = "AudioProjectEditorFullDataGrid";
        [ObservableProperty] private bool _showModdedStatesOnly;
        [ObservableProperty] private bool _isPlayAudioButtonEnabled = false;
        [ObservableProperty] private bool _isPasteEnabled = true;
        [ObservableProperty] private bool _isShowModdedStatesCheckBoxEnabled = false;
        [ObservableProperty] private bool _audioEditorVisibility = false;

        public AudioEditorViewModel(IAudioRepository audioRepository, PackFileService packFileService, IAudioProjectService audioProjectService)
        {
            _audioRepository = audioRepository;
            _packFileService = packFileService;
            _audioProjectService = audioProjectService;

            InitialiseCollections();

            _audioProjectService.BuildDialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository(_audioRepository.DialogueEventsWithStateGroups, _audioProjectService.DialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository);

            AudioProjectEditorFullDataGrid.CollectionChanged += OnAudioProjectEditorFullDataGridChanged;
        }

        private void OnAudioProjectEditorFullDataGridChanged(object audioProjectEditorFullDataGrid, NotifyCollectionChangedEventArgs e)
        {
            SetIsPasteEnabled(this, _audioProjectService);
        }

        public void SetAudioEditorVisibility(bool isVisible)
        {
            AudioEditorVisibility = isVisible;
        }

        partial void OnSelectedDialogueEventPresetChanged(string value)
        {
            ApplyDialogueEventPresetFiltering(this, _audioProjectService);
        }

        partial void OnShowEditedSoundBanksOnlyChanged(bool value)
        {
            if (value == true)
                AddEditedSoundBanksToAudioProjectTreeViewItemsWrappers(_audioProjectService);
            else if (value == false)
                AddAllSoundBanksToTreeViewItemsWrappers(_audioProjectService);
        }

        partial void OnShowEditedDialogueEventsOnlyChanged(bool value)
        {
            AddEditedDialogueEventsToSoundBankTreeViewItems(_audioProjectService.AudioProject, DialogueEventSoundBankFiltering, ShowEditedDialogueEventsOnly);
        }

        partial void OnShowModdedStatesOnlyChanged(bool value)
        {
            if (_selectedAudioProjectTreeItem is DialogueEvent selectedDialogueEvent)
            {
                LoadDialogueEventForAudioProjectEditorSingleRowDataGrid(this, _audioRepository, _audioProjectService, selectedDialogueEvent);
                LoadDialogueEventForAudioProjectEditorFullDataGrid(this, _audioRepository, _audioProjectService, selectedDialogueEvent);
                _logger.Here().Information($"Loaded DialogueEvent: {selectedDialogueEvent.Name}");
            }
        }

        public void OnSelectedAudioProjectTreeViewItemChanged(object value)
        {
            // Store the previous selected item.
            if (_selectedAudioProjectTreeItem != null)
                _previousSelectedAudioProjectTreeItem = _selectedAudioProjectTreeItem;
            _selectedAudioProjectTreeItem = value;

            // Set filtering properties.
            IsDialogueEventPresetFilterEnabled = false;
            SelectedDialogueEventPreset = null;

            // Handle item selection.
            if (_selectedAudioProjectTreeItem is SoundBank selectedSoundBank)
            {
                if (selectedSoundBank.Type == SoundBankType.ActionEventSoundBank.ToString())
                {
                    AudioProjectEditorLabel = $"Audio Project Editor - {selectedSoundBank.Name}";

                    HandleActionEventSoundBankSelection(this, _audioRepository, selectedSoundBank);

                    _logger.Here().Information($"Loaded Action Event SoundBank: {selectedSoundBank.Name}");
                }
                else if (selectedSoundBank.Type == SoundBankType.DialogueEventSoundBank.ToString())
                {
                    // Workaround for using ref with the MVVM toolkit as you can't pass a property by ref, so instead pass a field that is set to the property by ref then assign the ref field to the property.
                    var isDialogueEventPresetFilterEnabled = IsDialogueEventPresetFilterEnabled;
                    var dialogueEventPresets = DialogueEventPresets;
                    HandleDialogueEventsPresetFilter(selectedSoundBank.Name, ref dialogueEventPresets, DialogueEventSoundBankFiltering, SelectedDialogueEventPreset, ref isDialogueEventPresetFilterEnabled);
                    IsDialogueEventPresetFilterEnabled = isDialogueEventPresetFilterEnabled;
                    DialogueEventPresets = dialogueEventPresets;
                }
                else if (selectedSoundBank.Type == SoundBankType.MusicEventSoundBank.ToString())
                    HandleMusicEventSoundBankSelection();
            }
            else if (_selectedAudioProjectTreeItem is DialogueEvent selectedDialogueEvent)
            {
                AudioProjectEditorLabel = $"Audio Project Editor - {AddExtraUnderscoresToString(selectedDialogueEvent.Name)}";

                HandleDialogueEventSelection(this, _audioRepository, _audioProjectService, selectedDialogueEvent);

                _logger.Here().Information($"Loaded DialogueEvent: {selectedDialogueEvent.Name}");
            }
            else if (_selectedAudioProjectTreeItem is StateGroup selectedStateGroup)
            {
                AudioProjectEditorLabel = $"Audio Project Editor - {AddExtraUnderscoresToString(selectedStateGroup.Name)}";

                HandleStateGroupSelection(this, selectedStateGroup);

                _logger.Here().Information($"Loaded StateGroup: {selectedStateGroup.Name}");
            }

            SetIsPasteEnabled(this, _audioProjectService);
        }

        [RelayCommand] public void NewAudioProject()
        {
            NewAudioProjectWindow.Show(_packFileService, this, _audioProjectService);
        }

        [RelayCommand] public void SaveAudioProject()
        {
            _audioProjectService.SaveAudioProject(_packFileService);
        }

        [RelayCommand] public void LoadAudioProject()
        {
            _audioProjectService.LoadAudioProject(_packFileService, _audioRepository, this);
        }

        [RelayCommand] public void ResetFiltering()
        {
            // Workaround for using ref with the MVVM toolkit as you can't pass a property by ref, so instead pass a field that is set to the property by ref then assign the ref field to the property.
            var selectedDialogueEventPreset = SelectedDialogueEventPreset;
            ResetDialogueEventFiltering(DialogueEventSoundBankFiltering, ref selectedDialogueEventPreset, _audioProjectService);
            SelectedDialogueEventPreset = selectedDialogueEventPreset;

            AddAllDialogueEventsToSoundBankTreeViewItems(_audioProjectService.AudioProject, ShowEditedDialogueEventsOnly);
        }

        [RelayCommand] public void RemoveDataGridRowFromAudioProjectEditorFullDataGrid()
        {
            if (SelectedDataGridRows.Count == 1)
            {
                if (_selectedAudioProjectTreeItem is SoundBank selectedActionEventSoundBank)
                {
                    throw new NotImplementedException();
                }
                else if (_selectedAudioProjectTreeItem is DialogueEvent selectedDalogueEvent)
                    RemoveDataGridRowFromDialogueEvent(AudioProjectEditorFullDataGrid, SelectedDataGridRows[0], selectedDalogueEvent, _audioProjectService.DialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository);
            }
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
                    PlayWavFile(randomAudioFile);
                }
            }
        }

        [RelayCommand] public void CopyRows()
        {
            if (_selectedAudioProjectTreeItem is DialogueEvent)
                CopyDialogueEventRows(this, _audioProjectService);
        }

        [RelayCommand] public void PasteRows()
        {
            if (IsPasteEnabled && _selectedAudioProjectTreeItem is DialogueEvent selectedDalogueEvent)
                PasteDialogueEventRows(this, _audioProjectService, selectedDalogueEvent);
        }

        [RelayCommand] public void AddDataGridRowFromAudioProjectEditorSingleRowDataGridToFullDataGrid()
        {
            if (AudioProjectEditorSingleRowDataGrid.Count == 0)
                return;

            var newRow = ExtractRowFromDataGrid(AudioProjectEditorSingleRowDataGrid, _selectedAudioProjectTreeItem);
            InsertDataGridRowAlphabetically(AudioProjectEditorFullDataGrid, newRow);
            ClearDataGrid(AudioProjectEditorSingleRowDataGrid);

            if (_selectedAudioProjectTreeItem is SoundBank soundBank)
            {
                if (soundBank.ActionEvents != null)
                    HandleAddingActionEventDataGridRow(newRow, soundBank, AudioProjectEditorSingleRowDataGrid);
                else if (soundBank.MusicEvents != null)
                    HandleAddingMusicEventDataGridRow();
            }
            else if (_selectedAudioProjectTreeItem is DialogueEvent selectedDalogueEvent)
                HandleAddingDialogueEventDataGridRow(newRow, selectedDalogueEvent, AudioProjectEditorSingleRowDataGrid, _audioProjectService.DialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository);
            else if (_selectedAudioProjectTreeItem is StateGroup moddedStateGroup)
                HandleAddingStateGroupDataGridRow(newRow, moddedStateGroup, AudioProjectEditorSingleRowDataGrid);

        }

        public void ResetAudioEditorViewModelData()
        {
            AudioProjectEditorSingleRowDataGrid = null;
            AudioProjectEditorFullDataGrid = null;
            SelectedDataGridRows = null;
            CopiedDataGridRows = null;
            _selectedAudioProjectTreeItem = null;
            _previousSelectedAudioProjectTreeItem = null;
            DialogueEventSoundBankFiltering.Clear();
        }

        public void InitialiseCollections()
        {
            AudioProjectEditorSingleRowDataGrid = [];
            AudioProjectEditorFullDataGrid = [];
            SelectedDataGridRows = [];
            CopiedDataGridRows = [];
            DialogueEventPresets = [];
            AudioProjectTreeViewItems = _audioProjectService.AudioProject.AudioProjectTreeViewItems;
        }

        public void Close()
        {
            ResetAudioEditorViewModelData();
            _audioProjectService.ResetAudioProject();
        }

        public static bool Save() => true;

        public PackFile MainFile { get; set; }

        public bool HasUnsavedChanges { get; set; } = false;
    }
}
