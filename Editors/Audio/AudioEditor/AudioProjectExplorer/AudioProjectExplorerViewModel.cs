using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        private readonly ILogger _logger = Logging.Create<AudioProjectExplorerViewModel>();

        [ObservableProperty] private string _audioProjectExplorerLabel;
        [ObservableProperty] private bool _showEditedAudioProjectItemsOnly;
        [ObservableProperty] private bool _isDialogueEventPresetFilterEnabled = false;
        [ObservableProperty] private DialogueEventPreset? _selectedDialogueEventPreset;
        [ObservableProperty] private ObservableCollection<DialogueEventPreset> _dialogueEventPresets;
        [ObservableProperty] private string _searchQuery;
        [ObservableProperty] public ObservableCollection<TreeNode> _audioProjectTree = [];

        private ObservableCollection<TreeNode> _unfilteredTree;
        [ObservableProperty] public TreeNode _selectedNode;

        private readonly string _actionEventsContainerName = "Action Events";
        private readonly string _dialogueEventsContainerName = "Dialogue Events";
        private readonly string _stateGroupsContainerName = "State Groups";

        public AudioProjectExplorerViewModel(IEventHub eventHub, IAudioEditorService audioEditorService)
        {
            _eventHub = eventHub;
            _audioEditorService = audioEditorService;

            AudioProjectExplorerLabel = $"Audio Project Explorer";

            _audioEditorService.SelectedDialogueEventPreset = _selectedDialogueEventPreset;
            _audioEditorService.AudioProjectTree = _audioProjectTree;

            _eventHub.Register<InitialiseViewModelDataEvent>(this, InitialiseData);
            _eventHub.Register<ResetViewModelDataEvent>(this, ResetData);
            _eventHub.Register<SetAudioProjectExplorerLabelEvent>(this, SetLabel);
            _eventHub.Register<SetAudioProjectExplorerLabelEvent>(this, CreateAudioProjectTree);
        }

        public void InitialiseData(InitialiseViewModelDataEvent e)
        {
            DialogueEventPresets = [];
        }

        public void ResetData(ResetViewModelDataEvent e)
        {
            SelectedNode = null;
            AudioProjectTree.Clear();
        }

        public void SetLabel(SetAudioProjectExplorerLabelEvent setAudioProjectExplorerLabelEvent)
        {
            AudioProjectExplorerLabel = setAudioProjectExplorerLabelEvent.Label;
        }

        public void CreateAudioProjectTree(SetAudioProjectExplorerLabelEvent setAudioProjectExplorerLabelEvent)
        {
            var audioProject = _audioEditorService.AudioProject;

            AudioProjectTree.Clear();

            var actionEventsContainer = TreeNode.CreateContainer(_actionEventsContainerName, NodeType.ActionEventsContainer);
            var dialogueEventsContainer = TreeNode.CreateContainer(_dialogueEventsContainerName, NodeType.DialogueEventsContainer);
            var stateGroupsContainer = TreeNode.CreateContainer(_stateGroupsContainerName, NodeType.StateGroupsContainer);

            var actionEventSoundBanks = ShowEditedAudioProjectItemsOnly
                ? audioProject.GetEditedActionEventSoundBanks()
                : audioProject.GetActionEventSoundBanks();

            foreach (var actionEventSoundBank in actionEventSoundBanks)
            {
                var node = TreeNode.CreateChildNode(actionEventSoundBank.Name, NodeType.ActionEventSoundBank, actionEventsContainer);
                actionEventsContainer.Children.Add(node);
            }

            var dialogueEventSoundBanks = ShowEditedAudioProjectItemsOnly
                ? audioProject.GetEditedDialogueEventSoundBanks()
                : audioProject.GetDialogueEventSoundBanks();

            foreach (var dialogueEventSoundBank in dialogueEventSoundBanks)
            {
                var soundBankNode = TreeNode.CreateContainer(dialogueEventSoundBank.Name, NodeType.DialogueEventSoundBank, dialogueEventsContainer);

                var dialogueEvents = ShowEditedAudioProjectItemsOnly
                    ? dialogueEventSoundBank.GetEditedDialogueEvents()
                    : dialogueEventSoundBank.DialogueEvents;

                foreach (var dialogueEvent in dialogueEvents)
                {
                    var node = TreeNode.CreateChildNode(dialogueEvent.Name, NodeType.DialogueEvent, soundBankNode);
                    soundBankNode.Children.Add(node);
                }
                dialogueEventsContainer.Children.Add(soundBankNode);
            }

            var stateGroups = ShowEditedAudioProjectItemsOnly
                ? audioProject.GetEditedStateGroups()
                : audioProject.StateGroups;

            foreach (var stateGroup in stateGroups)
            {
                var node = TreeNode.CreateChildNode(stateGroup.Name, NodeType.StateGroup, stateGroupsContainer);
                stateGroupsContainer.Children.Add(node);
            }

            AudioProjectTree.Add(actionEventsContainer);
            AudioProjectTree.Add(dialogueEventsContainer);
            AudioProjectTree.Add(stateGroupsContainer);
            
            _unfilteredTree = new ObservableCollection<TreeNode>(AudioProjectTree);
        }

        partial void OnSelectedNodeChanged(TreeNode value)
        {
            _audioEditorService.SelectedExplorerNode = SelectedNode;

            _eventHub.Publish(new ExplorerNodeSelectedEvent(SelectedNode));

            ResetButtonEnablement();

            if (SelectedNode.IsDialogueEventSoundBank())
            {
                InitialiseDialogueEventPresetFilter();
                _logger.Here().Information($"Loaded Dialogue Event SoundBank: {SelectedNode.Name}");
            }
        }

        partial void OnSelectedDialogueEventPresetChanged(DialogueEventPreset? value)
        {
            ApplyDialogueEventPresetFiltering();
        }

        partial void OnSearchQueryChanged(string value)
        {
            if (_unfilteredTree == null)
                return;

            if (string.IsNullOrWhiteSpace(SearchQuery))
                ResetTree();
            else
                AudioProjectTree = FilterFileTree(SearchQuery);
        }

        private void ResetTree()
        {
            AudioProjectTree = new ObservableCollection<TreeNode>(_unfilteredTree);
        }

        private ObservableCollection<TreeNode> FilterFileTree(string query)
        {
            var filteredTree = new ObservableCollection<TreeNode>();

            foreach (var treeNode in _unfilteredTree)
            {
                var filteredNode = FilterTreeNode(treeNode, query);
                if (filteredNode != null)
                    filteredTree.Add(filteredNode);
            }

            return filteredTree;
        }

        private static TreeNode FilterTreeNode(TreeNode node, string query)
        {
            var matchesQuery = node.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            var filteredChildren = node.Children
                .Select(child => FilterTreeNode(child, query))
                .Where(child => child != null)
                .ToList();

            if (matchesQuery || filteredChildren.Count != 0)
            {
                var filteredNode = new TreeNode
                {
                    Name = node.Name,
                    NodeType = node.NodeType,
                    Parent = node.Parent,
                    Children = new ObservableCollection<TreeNode>(filteredChildren),
                    IsNodeExpanded = true
                };
                return filteredNode;
            }

            return null;
        }

        partial void OnShowEditedAudioProjectItemsOnlyChanged(bool value)
        {
            FilterEditedAudioProjectItems();
        }

        [RelayCommand] public void CollapseOrExpandAudioProjectTree() 
        {
            CollapseAndExpandNodes();
        }

        public void CollapseAndExpandNodes()
        {
            foreach (var node in AudioProjectTree)
            {
                node.IsNodeExpanded = !node.IsNodeExpanded;
                CollapseAndExpandNodesInner(node);
            }
        }

        public static void CollapseAndExpandNodesInner(TreeNode parentNode)
        {
            foreach (var node in parentNode.Children)
            {
                node.IsNodeExpanded = !node.IsNodeExpanded;
                CollapseAndExpandNodesInner(node);
            }
        }

        [RelayCommand] public void ClearText()
        {
            SearchQuery = "";
        }

        public void ApplyDialogueEventPresetFiltering()
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

        public void FilterEditedAudioProjectItems()
        {
            var audioProject = _audioEditorService.AudioProject;
            var editedActionEventSoundBanks = audioProject.GetEditedActionEventSoundBanks();
            var editedDialogueEventSoundBanks = audioProject.GetEditedDialogueEventSoundBanks();
            var editedStateGroups = audioProject.GetEditedStateGroups();

            foreach (var rootNode in AudioProjectTree)
                ProcessNode(rootNode, editedActionEventSoundBanks, editedDialogueEventSoundBanks, editedStateGroups);

            if (!ShowEditedAudioProjectItemsOnly)
            {
                var dialogueEventsContainer = TreeNode.GetNode(AudioProjectTree, _dialogueEventsContainerName);
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
            TreeNode node,
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
            TreeNode node,
            List<SoundBank> editedActionEventSoundBanks,
            List<SoundBank> editedDialogueEventSoundBanks,
            List<StateGroup> editedStateGroups)
        {
            return node.NodeType switch
            {
                NodeType.StateGroupsContainer => editedStateGroups.Count != 0,
                NodeType.ActionEventSoundBank => editedActionEventSoundBanks.Any(soundBank => soundBank.Name == node.Name),
                NodeType.DialogueEventSoundBank => editedDialogueEventSoundBanks.Any(soundBank => soundBank.Name == node.Name),
                NodeType.StateGroup => editedStateGroups.Any(stateGroup => stateGroup.Name == node.Name),
                NodeType.DialogueEvent => editedDialogueEventSoundBanks
                                        .Where(soundBank => soundBank.Name == node.Parent.Name)
                                        .SelectMany(soundBank => soundBank.DialogueEvents)
                                        .Any(dialogueEvent => dialogueEvent.Name == node.Name),
                _ => true,
            };
        }

        public void InitialiseDialogueEventPresetFilter()
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

        private static void ApplyDialogueEventVisibilityFilter(TreeNode soundBankNode, DialogueEventPreset? dialogueEventPreset)
        {
            var allowedNames = DialogueEventData
                .Where(dialogueEvent => SoundBanks.GetSoundBankSubTypeString(dialogueEvent.SoundBank) == soundBankNode.Name
                    && (!dialogueEventPreset.HasValue || dialogueEvent.DialogueEventPreset.Contains(dialogueEventPreset.Value)))
                .Select(dialogueEvent => dialogueEvent.Name)
                .ToHashSet();

            foreach (var dialogueEventNode in soundBankNode.Children)
                dialogueEventNode.IsVisible = allowedNames.Contains(dialogueEventNode.Name);
        }

        public void ResetDialogueEventFilterComboBoxSelectedItem(WatermarkComboBox watermarkComboBox)
        {
            watermarkComboBox.SelectedItem = null;
            SelectedDialogueEventPreset = null;
        }

        public void ResetButtonEnablement()
        {
            IsDialogueEventPresetFilterEnabled = false;
        }
    }
}
