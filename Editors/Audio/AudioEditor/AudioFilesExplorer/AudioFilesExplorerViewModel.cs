﻿using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.AudioProjectExplorer;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Presentation.Table;
using Editors.Audio.AudioEditor.UICommands;
using Editors.Audio.Utility;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Editors.Audio.AudioEditor.AudioFilesExplorer
{
    public partial class AudioFilesExplorerViewModel : ObservableObject
    {
        private readonly IGlobalEventHub _globalEventHub;
        private readonly IEventHub _eventHub;
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IPackFileService _packFileService;
        private readonly IAudioEditorService _audioEditorService;
        private readonly IAudioFilesTreeBuilderService _audioFilesTreeBuilder;
        private readonly IAudioFilesTreeSearchFilterService _audioFilesTreeFilter;
        private readonly SoundPlayer _soundPlayer;

        [ObservableProperty] private string _audioFilesExplorerLabel;
        [ObservableProperty] private bool _isAddAudioFilesButtonEnabled = false;
        [ObservableProperty] private bool _isPlayAudioButtonEnabled = false;
        [ObservableProperty] private string _filterQuery;
        [ObservableProperty] private ObservableCollection<AudioFilesTreeNode> _audioFilesTree;
        private ObservableCollection<AudioFilesTreeNode> _unfilteredTree;

        public ObservableCollection<AudioFilesTreeNode> SelectedTreeNodes { get; set; } = [];

        public AudioFilesExplorerViewModel(
            IGlobalEventHub globalEventHub,
            IEventHub eventHub,
            IUiCommandFactory uiCommandFactory,
            IPackFileService packFileService,
            IAudioEditorService audioEditorService,
            IAudioFilesTreeBuilderService audioFilesTreeBuilder,
            IAudioFilesTreeSearchFilterService audioFilesTreeFilter,
            SoundPlayer soundPlayer)
        {
            _globalEventHub = globalEventHub;
            _eventHub = eventHub;
            _uiCommandFactory = uiCommandFactory;
            _packFileService = packFileService;
            _audioEditorService = audioEditorService;
            _audioFilesTreeBuilder = audioFilesTreeBuilder;
            _audioFilesTreeFilter = audioFilesTreeFilter;
            _soundPlayer = soundPlayer;

            SelectedTreeNodes.CollectionChanged += OnSelectedTreeNodesChanged;

            _eventHub.Register<AudioProjectExplorerNodeSelectedEvent>(this, OnAudioProjectExplorerNodeSelected);
            _globalEventHub.Register<PackFileContainerFilesAddedEvent>(this, x => RefreshAudioFilesTree(x.Container));
            _globalEventHub.Register<PackFileContainerFilesRemovedEvent>(this, x => RefreshAudioFilesTree(x.Container));
            _globalEventHub.Register<PackFileContainerFolderRemovedEvent>(this, x => RefreshAudioFilesTree(x.Container));

            var editablePack = _packFileService.GetEditablePack();
            if (editablePack == null)
                return;

            AudioFilesExplorerLabel = $"Audio Files Explorer - {TableHelpers.DuplicateUnderscores(editablePack.Name)}";

            AudioFilesTree = _audioFilesTreeBuilder.BuildTree(editablePack); ;
            _unfilteredTree = new ObservableCollection<AudioFilesTreeNode>(AudioFilesTree);
        }

        private void OnAudioProjectExplorerNodeSelected(AudioProjectExplorerNodeSelectedEvent e) => ResetButtonEnablement();

        private void ResetButtonEnablement()
        {
            IsAddAudioFilesButtonEnabled = false;
        }

        private void RefreshAudioFilesTree(PackFileContainer packFileContainer)
        {
            AudioFilesTree = _audioFilesTreeBuilder.BuildTree(packFileContainer); ;
            _unfilteredTree = new ObservableCollection<AudioFilesTreeNode>(AudioFilesTree);
        }

        private void OnSelectedTreeNodesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (AudioFilesTreeNode addedNode in e.NewItems)
                {
                    if (addedNode.NodeType != AudioFilesTreeNodeType.WavFile)
                        SelectedTreeNodes.Remove(addedNode);
                }
            }

            SetButtonEnablement();
        }

        private void SetButtonEnablement()
        {
            IsPlayAudioButtonEnabled = SelectedTreeNodes.Count == 1;

            var selectedAudioProjectExplorerNode = _audioEditorService.SelectedAudioProjectExplorerNode;
            if (selectedAudioProjectExplorerNode == null)
                return;

            if (SelectedTreeNodes.Count > 0)
            {
                if (selectedAudioProjectExplorerNode.NodeType == AudioProjectTreeNodeType.ActionEventSoundBank || selectedAudioProjectExplorerNode.NodeType == AudioProjectTreeNodeType.DialogueEvent)
                    IsAddAudioFilesButtonEnabled = true;
            }
            else
                IsAddAudioFilesButtonEnabled = false;
        }

        partial void OnFilterQueryChanged(string value)
        {
            _audioFilesTreeFilter.FilterTree(AudioFilesTree, FilterQuery);
        }

        [RelayCommand] public void CollapseOrExpandAudioFilesTree()
        {
            if (AudioFilesTree == null || AudioFilesTree.Count == 0)
                return;

            var isExpanded = AudioFilesTree.Any(node => node.IsNodeExpanded);

            foreach (var rootNode in AudioFilesTree)
                ToggleNodeExpansion(rootNode, !isExpanded);
        }

        private static void ToggleNodeExpansion(AudioFilesTreeNode node, bool shouldExpand)
        {
            node.IsNodeExpanded = shouldExpand;

            foreach (var child in node.Children)
                ToggleNodeExpansion(child, shouldExpand);
        }

        [RelayCommand] public void SetAudioFiles()
        {
            _uiCommandFactory.Create<SetAudioFilesCommand>().Execute(SelectedTreeNodes);
        }

        [RelayCommand] public void PlayWav()
        {
            if (!IsPlayAudioButtonEnabled)
                return;

            var selectedAudioFile = SelectedTreeNodes[0];
            _uiCommandFactory.Create<PlayAudioFileCommand>().Execute(selectedAudioFile);
        }

        [RelayCommand] public void ClearText()
        {
            FilterQuery = string.Empty;
        }
    }
}
