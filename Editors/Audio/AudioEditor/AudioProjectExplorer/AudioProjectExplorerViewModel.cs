using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.AudioProjectExplorer.Filters;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Models;
using Editors.Audio.GameSettings.Warhammer3;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Xceed.Wpf.Toolkit;
using static Editors.Audio.GameSettings.Warhammer3.DialogueEvents;

namespace Editors.Audio.AudioEditor.AudioProjectExplorer
{
    public partial class AudioProjectExplorerViewModel : ObservableObject
    {
        private readonly IEventHub _eventHub;
        private readonly IAudioEditorService _audioEditorService;
        private readonly IAudioProjectTreeBuilderService _audioProjectTreeBuilder;
        private readonly IAudioProjectTreeFilterService _audioProjectTreeFilterService;

        private readonly ILogger _logger = Logging.Create<AudioProjectExplorerViewModel>();

        [ObservableProperty] private string _audioProjectExplorerLabel;
        [ObservableProperty] private bool _showEditedAudioProjectItemsOnly;
        [ObservableProperty] private bool _isDialogueEventPresetFilterEnabled = false;
        [ObservableProperty] private DialogueEventPreset? _selectedDialogueEventPreset;
        [ObservableProperty] private ObservableCollection<DialogueEventPreset> _dialogueEventPresets = [];
        [ObservableProperty] private string _searchQuery;
        [ObservableProperty] public ObservableCollection<AudioProjectTreeNode> _audioProjectTree = [];

        private ObservableCollection<AudioProjectTreeNode> _unfilteredTree;
        [ObservableProperty] public AudioProjectTreeNode _selectedNode;

        public AudioProjectExplorerViewModel(
            IEventHub eventHub,
            IAudioEditorService audioEditorService,
            IAudioProjectTreeBuilderService audioProjectTreeBuilder,
            IAudioProjectTreeFilterService audioProjectTreeFilterService)
        {
            _eventHub = eventHub;
            _audioEditorService = audioEditorService;
            _audioProjectTreeBuilder = audioProjectTreeBuilder;
            _audioProjectTreeFilterService = audioProjectTreeFilterService;

            AudioProjectExplorerLabel = $"Audio Project Explorer";

            _audioEditorService.SelectedDialogueEventPreset = _selectedDialogueEventPreset;
            _audioEditorService.AudioProjectTree = _audioProjectTree;

            _eventHub.Register<AudioProjectInitialisedEvent>(this, OnAudioProjectInitialised);
        }

        private void OnAudioProjectInitialised(AudioProjectInitialisedEvent e)
        {
            AudioProjectTree = _audioProjectTreeBuilder.BuildTree(_audioEditorService.AudioProject, ShowEditedAudioProjectItemsOnly);
            SetLabel(e.Label);
        }

        private void CreateAudioProjectTree()
        {
            var audioProject = _audioEditorService.AudioProject;

            AudioProjectTree.Clear();

            var actionEventsContainer = AudioProjectTreeNode.CreateContainerNode(AudioProjectTreeInfo.ActionEventSoundBanksContainerName, AudioProjectTreeNodeType.ActionEventSoundBanksContainer);
            var dialogueEventsContainer = AudioProjectTreeNode.CreateContainerNode(AudioProjectTreeInfo.DialogueEventSoundBanksContainer, AudioProjectTreeNodeType.DialogueEventSoundBanksContainer);
            var stateGroupsContainer = AudioProjectTreeNode.CreateContainerNode(AudioProjectTreeInfo.StateGroupsContainerName, AudioProjectTreeNodeType.StateGroupsContainer);

            var actionEventSoundBanks = ShowEditedAudioProjectItemsOnly
                ? audioProject.GetEditedActionEventSoundBanks()
                : audioProject.GetActionEventSoundBanks();

            foreach (var actionEventSoundBank in actionEventSoundBanks)
            {
                var node = AudioProjectTreeNode.CreateChildNode(actionEventSoundBank.Name, AudioProjectTreeNodeType.ActionEventSoundBank, actionEventsContainer);
                actionEventsContainer.Children.Add(node);
            }

            var dialogueEventSoundBanks = ShowEditedAudioProjectItemsOnly
                ? audioProject.GetEditedDialogueEventSoundBanks()
                : audioProject.GetDialogueEventSoundBanks();

            foreach (var dialogueEventSoundBank in dialogueEventSoundBanks)
            {
                var soundBankNode = AudioProjectTreeNode.CreateContainerNode(dialogueEventSoundBank.Name, AudioProjectTreeNodeType.DialogueEventSoundBank, dialogueEventsContainer);

                var dialogueEvents = ShowEditedAudioProjectItemsOnly
                    ? dialogueEventSoundBank.GetEditedDialogueEvents()
                    : dialogueEventSoundBank.DialogueEvents;

                foreach (var dialogueEvent in dialogueEvents)
                {
                    var node = AudioProjectTreeNode.CreateChildNode(dialogueEvent.Name, AudioProjectTreeNodeType.DialogueEvent, soundBankNode);
                    soundBankNode.Children.Add(node);
                }
                dialogueEventsContainer.Children.Add(soundBankNode);
            }

            var stateGroups = ShowEditedAudioProjectItemsOnly
                ? audioProject.GetEditedStateGroups()
                : audioProject.StateGroups;

            foreach (var stateGroup in stateGroups)
            {
                var node = AudioProjectTreeNode.CreateChildNode(stateGroup.Name, AudioProjectTreeNodeType.StateGroup, stateGroupsContainer);
                stateGroupsContainer.Children.Add(node);
            }

            AudioProjectTree.Add(actionEventsContainer);
            AudioProjectTree.Add(dialogueEventsContainer);
            AudioProjectTree.Add(stateGroupsContainer);
            
            _unfilteredTree = new ObservableCollection<AudioProjectTreeNode>(AudioProjectTree);
        }

        private void SetLabel(string label)
        {
            AudioProjectExplorerLabel = label;
        }

        partial void OnSelectedNodeChanged(AudioProjectTreeNode value)
        {
            _audioEditorService.SelectedAudioProjectExplorerNode = SelectedNode;

            _eventHub.Publish(new AudioProjectExplorerNodeSelectedEvent(SelectedNode));

            ResetButtonEnablement();

            if (SelectedNode.IsDialogueEventSoundBank())
            {
                InitialiseDialogueEventPresetFilter();
                _logger.Here().Information($"Loaded Dialogue Event SoundBank: {SelectedNode.Name}");
            }
        }

        private void ReapplyFilters()
        {
            if (AudioProjectTree is null)
                return;

            var opts = new AudioProjectTreeFilterOptions(
                SearchQuery,
                ShowEditedAudioProjectItemsOnly,
                _audioEditorService.AudioProject);

            _audioProjectTreeFilterService.Apply(AudioProjectTree, opts);
        }

        partial void OnSelectedDialogueEventPresetChanged(DialogueEventPreset? value)
        {
            // The preset belongs to a Dialogue-Event SoundBank node only
            if (SelectedNode?.IsDialogueEventSoundBank() != true)
                return;

            // 1️⃣  persist so it survives other filter toggles
            SelectedNode.PresetFilter = value ?? DialogueEventPreset.ShowAll;

            // 2️⃣  update the “ (Filtered by …)” text the UI binds to
            SelectedNode.PresetFilterDisplayText =
                value.HasValue && value.Value != DialogueEventPreset.ShowAll
                    ? $" (Filtered by {GetDialogueEventPresetDisplayString(value.Value)} preset)"
                    : null;

            ReapplyFilters();   // apply all active rules again
        }

        partial void OnSearchQueryChanged(string value)
        {
            ReapplyFilters();
        }

        private ObservableCollection<AudioProjectTreeNode> FilterFileTree(string query)
        {
            var filteredTree = new ObservableCollection<AudioProjectTreeNode>();

            foreach (var treeNode in _unfilteredTree)
            {
                var filteredNode = FilterTreeNode(treeNode, query);
                if (filteredNode != null)
                    filteredTree.Add(filteredNode);
            }

            return filteredTree;
        }

        private static AudioProjectTreeNode FilterTreeNode(AudioProjectTreeNode node, string query)
        {
            var matchesQuery = node.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            var filteredChildren = node.Children
                .Select(child => FilterTreeNode(child, query))
                .Where(child => child != null)
                .ToList();

            if (matchesQuery || filteredChildren.Count != 0)
            {
                var filteredNode = new AudioProjectTreeNode
                {
                    Name = node.Name,
                    NodeType = node.NodeType,
                    Parent = node.Parent,
                    Children = new ObservableCollection<AudioProjectTreeNode>(filteredChildren),
                    IsNodeExpanded = true
                };
                return filteredNode;
            }

            return null;
        }

        partial void OnShowEditedAudioProjectItemsOnlyChanged(bool value)
        {
            ReapplyFilters();
        }

        [RelayCommand] public void CollapseOrExpandAudioProjectTree() 
        {
            CollapseAndExpandNodes();
        }

        private void CollapseAndExpandNodes()
        {
            foreach (var node in AudioProjectTree)
            {
                node.IsNodeExpanded = !node.IsNodeExpanded;
                CollapseAndExpandNodesInner(node);
            }
        }

        private static void CollapseAndExpandNodesInner(AudioProjectTreeNode parentNode)
        {
            foreach (var node in parentNode.Children)
            {
                node.IsNodeExpanded = !node.IsNodeExpanded;
                CollapseAndExpandNodesInner(node);
            }
        }

        [RelayCommand] public void ClearFilterText()
        {
            SearchQuery = "";
        }

        private void ApplyDialogueEventPresetFiltering()
        {
            if (!SelectedNode.IsDialogueEventSoundBank() || SelectedDialogueEventPreset == null)
                return;

            SelectedNode.PresetFilter = SelectedDialogueEventPreset;
            if (SelectedDialogueEventPreset != DialogueEventPreset.ShowAll)
                SelectedNode.PresetFilterDisplayText = $" (Filtered by {GetDialogueEventPresetDisplayString(SelectedDialogueEventPreset)} preset)";
            else
                SelectedNode.PresetFilterDisplayText = null;

            ApplyDialogueEventVisibilityFilter(SelectedNode, SelectedDialogueEventPreset);
        }

        private void FilterEditedAudioProjectItems()
        {
            var audioProject = _audioEditorService.AudioProject;
            var editedActionEventSoundBanks = audioProject.GetEditedActionEventSoundBanks();
            var editedDialogueEventSoundBanks = audioProject.GetEditedDialogueEventSoundBanks();
            var editedStateGroups = audioProject.GetEditedStateGroups();

            foreach (var rootNode in AudioProjectTree)
                ProcessNode(rootNode, editedActionEventSoundBanks, editedDialogueEventSoundBanks, editedStateGroups);

            if (!ShowEditedAudioProjectItemsOnly)
            {
                var dialogueEventsContainer = AudioProjectTreeNode.GetNode(AudioProjectTree, AudioProjectTreeInfo.DialogueEventSoundBanksContainer);
                if (dialogueEventsContainer == null) 
                    return;

                foreach (var soundBankNode in dialogueEventsContainer.Children)
                {
                    if (soundBankNode.PresetFilter.HasValue && soundBankNode.PresetFilter.Value != DialogueEventPreset.ShowAll)
                        ApplyDialogueEventVisibilityFilter(soundBankNode, soundBankNode.PresetFilter);
                }
            }
        }

        private void ProcessNode(
            AudioProjectTreeNode node,
            List<SoundBank> editedActionEventSoundBanks,
            List<SoundBank> editedDialogueEventSoundBanks,
            List<StateGroup> editedStateGroups)
        {
            foreach (var child in node.Children)
                ProcessNode(child, editedActionEventSoundBanks, editedDialogueEventSoundBanks, editedStateGroups);

            var isVisible = !ShowEditedAudioProjectItemsOnly
                || IsNodeEdited(node, editedActionEventSoundBanks, editedDialogueEventSoundBanks, editedStateGroups);

            if (node.Children.Any())
                isVisible &= node.Children.Any(c => c.IsVisible);

            node.IsVisible = isVisible;
        }

        private static bool IsNodeEdited(
            AudioProjectTreeNode node,
            List<SoundBank> editedActionEventSoundBanks,
            List<SoundBank> editedDialogueEventSoundBanks,
            List<StateGroup> editedStateGroups)
        {
            return node.NodeType switch
            {
                AudioProjectTreeNodeType.StateGroupsContainer => editedStateGroups.Count != 0,
                AudioProjectTreeNodeType.ActionEventSoundBank => editedActionEventSoundBanks.Any(soundBank => soundBank.Name == node.Name),
                AudioProjectTreeNodeType.DialogueEventSoundBank => editedDialogueEventSoundBanks.Any(soundBank => soundBank.Name == node.Name),
                AudioProjectTreeNodeType.StateGroup => editedStateGroups.Any(stateGroup => stateGroup.Name == node.Name),
                AudioProjectTreeNodeType.DialogueEvent => editedDialogueEventSoundBanks
                    .Where(soundBank => soundBank.Name == node.Parent.Name)
                    .SelectMany(soundBank => soundBank.DialogueEvents)
                    .Any(dialogueEvent => dialogueEvent.Name == node.Name),
                _ => true,
            };
        }

        private void InitialiseDialogueEventPresetFilter()
        {
            var soundBankSubtype = SoundBanks.GetSoundBankSubtype(SelectedNode.Name);

            DialogueEventPresets = new ObservableCollection<DialogueEventPreset>(DialogueEventData
                .Where(dialogueEvent => dialogueEvent.SoundBank == soundBankSubtype)
                .SelectMany(dialogueEvent => dialogueEvent.DialogueEventPreset)
                .Distinct());

            SelectedDialogueEventPreset = SelectedNode.PresetFilter != DialogueEventPreset.ShowAll
                    ? SelectedNode.PresetFilter
                    : null;

            IsDialogueEventPresetFilterEnabled = true;
        }

        private static void ApplyDialogueEventVisibilityFilter(AudioProjectTreeNode soundBankNode, DialogueEventPreset? dialogueEventPreset)
        {
            var allowedNames = DialogueEventData
                .Where(dialogueEvent => SoundBanks.GetSoundBankSubTypeString(dialogueEvent.SoundBank) == soundBankNode.Name
                    && (!dialogueEventPreset.HasValue || dialogueEvent.DialogueEventPreset.Contains(dialogueEventPreset.Value)))
                .Select(dialogueEvent => dialogueEvent.Name)
                .ToHashSet();

            foreach (var dialogueEventNode in soundBankNode.Children)
                dialogueEventNode.IsVisible = allowedNames.Contains(dialogueEventNode.Name);
        }

        private void ResetTree()
        {
            AudioProjectTree = new ObservableCollection<AudioProjectTreeNode>(_unfilteredTree);
        }

        public void ResetDialogueEventFilterComboBoxSelectedItem(WatermarkComboBox watermarkComboBox)
        {
            watermarkComboBox.SelectedItem = null;
            SelectedDialogueEventPreset = null;
        }

        private void ResetButtonEnablement()
        {
            IsDialogueEventPresetFilterEnabled = false;
        }
    }
}
