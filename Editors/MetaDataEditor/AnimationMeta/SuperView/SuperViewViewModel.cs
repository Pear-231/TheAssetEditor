using CommunityToolkit.Mvvm.ComponentModel;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using Microsoft.Xna.Framework;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.Db;

namespace Editors.AnimationMeta.SuperView
{
    public partial class SuperViewViewModel : EditorHostBase, ISaveableEditor
    {
        private const string AudioMetadataTagsTableName = "audio_metadata_tags_tables";
        private const int AudioTabIndex = 2;

        private static readonly string[] s_audioMetadataTagKeyColumns = ["key", "Key"];
        private static readonly string[] s_audioMetadataTagBattleStartColumns = ["sound_event_battle_start", "Sound Event Battle Start"];

        private readonly ILogger _logger = Logging.Create<SuperViewViewModel>();
        SceneObjectViewModel _asset;

        private readonly SceneObjectEditor _sceneObjectBuilder;
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly IMetaDataBuilder _metaDataFactory;
        private readonly IPackFileService _packFileService;
        private readonly IDbTableQueryService _dbTableQueryService;
        private readonly IEventHub _eventHub;
        private readonly IUiCommandFactory _uiCommandFactory;

        public DbTable? DebugAudioMetadataTagsTable { get; private set; }

        [ObservableProperty] string _persistentMetaFilePath = "";
        [ObservableProperty] string _metaFilePath = "";
        [ObservableProperty] MetaDataEditorViewModel _persistentMetaEditor;
        [ObservableProperty] MetaDataEditorViewModel _metaEditor;
        [ObservableProperty] MetaDataEditorViewModel _audioMetaEditor;
        [ObservableProperty] int _selectedTabControllerIndex = 0;
        public override Type EditorViewModelType => typeof(EditorView);
        public bool HasUnsavedChanges
        { 
            get 
            {
                return PersistentMetaEditor.HasUnsavedChanges || MetaEditor.HasUnsavedChanges || AudioMetaEditor.HasUnsavedChanges;
            }
            set 
            {
                PersistentMetaEditor.HasUnsavedChanges = value;
                MetaEditor.HasUnsavedChanges = value;
                AudioMetaEditor.HasUnsavedChanges = value;
            } 
        }


        public SuperViewViewModel(
            IPackFileService packFileService,
            IEventHub eventHub,
            IUiCommandFactory uiCommandFactory,
            SceneObjectEditor sceneObjectBuilder,
            IEditorHostParameters editorHostParameters,
            MetaDataFileParser metaDataFileParser,
            IMetaDataBuilder metaDataFactory,
            IDbTableQueryService dbTableQueryService)
            : base(editorHostParameters)
        {
            DisplayName = "Super view";
            _packFileService = packFileService;
            _eventHub = eventHub;
            _uiCommandFactory = uiCommandFactory;
            _sceneObjectBuilder = sceneObjectBuilder;
            _metaDataFileParser = metaDataFileParser;
            _metaDataFactory = metaDataFactory;
            _dbTableQueryService = dbTableQueryService;
            Initialize();
            eventHub.Register<ScopedFileSavedEvent>(this, OnFileSaved);
            eventHub.Register<SceneObjectUpdateEvent>(this, OnSceneObjectUpdated);
            eventHub.Register<MetaDataAttributeChangedEvent>(this, OnMetaDataAttributeChanged);
            eventHub.Register<SelecteMetaDataAttributeChangedEvent>(this, OnSelectedMetaDataAttributeChanged);
        }

        private void OnSelectedMetaDataAttributeChanged(SelecteMetaDataAttributeChangedEvent @event)
        {
            RecreateMetaDataInformation();
            PrintAudioSoundEventLookup();
        }
        void OnMetaDataAttributeChanged(MetaDataAttributeChangedEvent @event) => RecreateMetaDataInformation();
        void OnMetaDataChanged(SceneObject sceneObject) => RecreateMetaDataInformation();

        private void PrintAudioSoundEventLookup()
        {
            if (SelectedTabControllerIndex != AudioTabIndex)
                return;

            var audioMetaDataFile = _asset.Data.AudioMetaData;
            if (audioMetaDataFile == null)
            {
                _logger.Here().Information("No audio metadata file is loaded on the current asset.");
                return;
            }

            var containers = _packFileService.GetAllPackfileContainers();
            var audioMetadataTagsTables = _dbTableQueryService.LoadTables(AudioMetadataTagsTableName, containers);
            var audioMetadataTagsTable = audioMetadataTagsTables.FirstOrDefault();
            DebugAudioMetadataTagsTable = audioMetadataTagsTable;

            if (AudioMetaEditor.SelectedAttribute is not SoundTrigger_v10 soundTrigger)
                return;

            if (string.IsNullOrWhiteSpace(soundTrigger.SoundEvent))
                return;

            if (audioMetadataTagsTables.Count == 0)
            {
                _logger.Here().Information("Unable to load table '{TableName}'", AudioMetadataTagsTableName);
                return;
            }

            var battleStartActionEvent = string.Empty;
            var foundMapping = false;
            foreach (var currentTable in audioMetadataTagsTables)
            {
                if (!TryResolveBattleStartActionEvent(currentTable, soundTrigger.SoundEvent, out battleStartActionEvent))
                    continue;

                foundMapping = true;
                break;
            }

            if (foundMapping)
            {
                _logger.Here().Information(
                    "Audio metadata key '{SoundEventKey}' maps to battle start action event '{BattleStartActionEvent}'",
                    soundTrigger.SoundEvent,
                    battleStartActionEvent);
            }
            else
            {
                _logger.Here().Information(
                    "No audio metadata battle start mapping found for key '{SoundEventKey}' in table '{TableName}'",
                    soundTrigger.SoundEvent,
                    AudioMetadataTagsTableName);
            }
        }

        private bool TryResolveBattleStartActionEvent(DbTable audioMetadataTagsTable, string soundEventKey, out string battleStartActionEvent)
        {
            battleStartActionEvent = string.Empty;

            foreach (var row in audioMetadataTagsTable.Rows)
            {
                var rowKey = string.Empty;
                foreach (var keyColumn in s_audioMetadataTagKeyColumns)
                {
                    rowKey = row.GetString(keyColumn) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(rowKey) == false)
                        break;
                }

                if (!string.Equals(rowKey, soundEventKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var battleStartColumn in s_audioMetadataTagBattleStartColumns)
                {
                    battleStartActionEvent = row.GetString(battleStartColumn) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(battleStartActionEvent) == false)
                        return true;
                }

                return false;
            }

            return false;
        }

        private void OnFileSaved(ScopedFileSavedEvent evnt)
        {
            var newFile = _packFileService.FindFile(evnt.NewPath);
            if (evnt.FileOwner == PersistentMetaEditor)
                _sceneObjectBuilder.SetMetaFile(_asset.Data, _asset.Data.MetaData, newFile, _asset.Data.AudioMetaData);
            else if (evnt.FileOwner == MetaEditor)
                _sceneObjectBuilder.SetMetaFile(_asset.Data, newFile, _asset.Data.PersistMetaData, _asset.Data.AudioMetaData);
            else if (evnt.FileOwner == AudioMetaEditor)
                _sceneObjectBuilder.SetMetaFile(_asset.Data, _asset.Data.MetaData, _asset.Data.PersistMetaData, newFile);
            else
                throw new Exception($"Unable to determine file owner when reciving a file save event in SuperView. Owner:{evnt.FileOwner}, File:{evnt.NewPath}");
        }

        void Initialize()
        {
            PersistentMetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            MetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            AudioMetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            
            var assetViewModel = _sceneObjectViewModelBuilder.CreateAsset("SuperViewRoot", true, "Root", Color.Black,null);
            SceneObjects.Add(assetViewModel);

            assetViewModel.Data.MetaDataChanged += OnMetaDataChanged;

            _asset = assetViewModel;
            OnSceneObjectUpdated(new SceneObjectUpdateEvent(_asset.Data, false, false, false, true));
        }

        void RecreateMetaDataInformation()
        {
            foreach (var item in SceneObjects)
            {
                foreach (var t in item.Data.MetaDataItems)
                    t.CleanUp();

                item.Data.MetaDataItems.Clear();
                item.Data.Player.AnimationRules.Clear();
            }

            var persist = PersistentMetaEditor.ParsedFile;
            var meta = MetaEditor.ParsedFile;

            _asset.Data.MetaDataItems = _metaDataFactory.Create(persist, meta, MetaEditor.SelectedAttribute, _asset.Data.MainNode, _asset.Data, _asset.Data.Player, _asset.FragAndSlotSelection.FragmentList.SelectedItem);
            _asset.Data.Player.Refresh();
        }

        private void OnSceneObjectUpdated(SceneObjectUpdateEvent e)
        {
            PersistentMetaEditor.LoadFile(e.Owner.PersistMetaData);
            MetaEditor.LoadFile(e.Owner.MetaData);
            AudioMetaEditor.LoadFile(e.Owner.AudioMetaData);

            RecreateMetaDataInformation();
        }

        public void Load(AnimationToolInput debugDataToLoad)
        {
            _sceneObjectBuilder.SetMesh(_asset.Data, debugDataToLoad.Mesh);

            // Hack :(
            if (debugDataToLoad.AnimationSlot != null)
            {
                var frag = _asset.FragAndSlotSelection.FragmentList.PossibleValues.FirstOrDefault(x => x.FullPath == debugDataToLoad.FragmentName);
                _asset.FragAndSlotSelection.FragmentList.SelectedItem = frag;

                var slot = _asset.FragAndSlotSelection.FragmentSlotList.PossibleValues.First(x => x.SlotName == debugDataToLoad.AnimationSlot.Value);
                _asset.FragAndSlotSelection.FragmentSlotList.SelectedItem = slot;
            }
        }


        public void RefreshAction() => _asset.Data.TriggerMeshChanged();

        public override void Close()
        {
            _eventHub?.UnRegister(this);
            base.Close();
        }

        public bool Save()
        {
            var res0 = PersistentMetaEditor.Save();
            var res1 = MetaEditor.Save();
            var res2 = AudioMetaEditor.Save();
            return res0 && res1 && res2;
        }
    }
}
