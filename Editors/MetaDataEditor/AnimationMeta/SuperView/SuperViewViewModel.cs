using CommunityToolkit.Mvvm.ComponentModel;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using Microsoft.Xna.Framework;
using System.IO;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.DB;

namespace Editors.AnimationMeta.SuperView
{
    public partial class SuperViewViewModel : EditorHostBase, ISaveableEditor
    {
        private const string AudioMetadataTagsTableName = "audio_metadata_tags_tables";
        private const string LandUnitsTableName = "land_units_tables";
        private const string UnitArmourTypesTableName = "unit_armour_types_tables";
        private const string UnitVariantsTableName = "unit_variants_tables";
        private const string VariantsTableName = "variants_tables";
        private const int AudioTabIndex = 2;

        private readonly ILogger _logger = Logging.Create<SuperViewViewModel>();
        SceneObjectViewModel _asset;

        private readonly SceneObjectEditor _sceneObjectBuilder;
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly IMetaDataBuilder _metaDataFactory;
        private readonly IPackFileService _packFileService;
        private readonly IDbTableQueryService _dbTableQueryService;
        private readonly IEventHub _eventHub;
        private readonly IUiCommandFactory _uiCommandFactory;
        private Dictionary<string, string>? _battleEventsByAudioMetadataTagKey;
        private Dictionary<string, string>? _armourAudioTypesByVariantMeshName;
        private string _variantMeshName = "";

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
            UpdateAudioSoundEventDatabaseKey();
        }
        void OnMetaDataAttributeChanged(MetaDataAttributeChangedEvent @event)
        {
            RecreateMetaDataInformation();
            UpdateAudioSoundEventDatabaseKey();
        }
        void OnMetaDataChanged(SceneObject sceneObject) => RecreateMetaDataInformation();

        partial void OnSelectedTabControllerIndexChanged(int value)
        {
            if (value == AudioTabIndex)
                UpdateAudioSoundEventDatabaseKey();
        }

        private void UpdateAudioSoundEventDatabaseKey()
        {
            if (SelectedTabControllerIndex != AudioTabIndex)
                return;

            if (AudioMetaEditor.SelectedAttribute is not SoundTrigger_v10 soundTrigger)
                return;

            if (string.IsNullOrWhiteSpace(soundTrigger.SoundEvent))
                return;

            try
            {
                _battleEventsByAudioMetadataTagKey ??= LoadBattleEventsByAudioMetadataTagKey();
                _battleEventsByAudioMetadataTagKey.TryGetValue(soundTrigger.SoundEvent, out var battleEvent);
                _armourAudioTypesByVariantMeshName ??= LoadArmourAudioTypesByVariantMeshName();
                _armourAudioTypesByVariantMeshName.TryGetValue(_variantMeshName, out var armourAudioType);

                var soundEventVariable = AudioMetaEditor.SelectedTag?.Variables
                    .FirstOrDefault(x => x.FieldName == "Sound Event");
                if (soundEventVariable != null)
                {
                    var battleEventDisplay = battleEvent ?? "not found";
                    var armourAudioTypeDisplay = armourAudioType ?? "not found";
                    soundEventVariable.RelatedValue =
                        $"Battle sound event: {battleEventDisplay}\nArmour audio type: {armourAudioTypeDisplay}";
                }
            }
            catch (Exception exception)
            {
                _logger.Here().Warning(exception, "Unable to resolve related audio information for sound event '{SoundEvent}'", soundTrigger.SoundEvent);
            }
        }

        private Dictionary<string, string> LoadBattleEventsByAudioMetadataTagKey()
        {
            var battleEventsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tables = _dbTableQueryService.LoadTables(AudioMetadataTagsTableName, _packFileService.GetAllPackfileContainers());

            foreach (var row in tables.SelectMany(x => x.Rows))
            {
                var key = row.GetString("key");
                var battleEvent = row.GetString("sound_event_battle_start");
                if (!string.IsNullOrWhiteSpace(battleEvent) && !string.IsNullOrWhiteSpace(key))
                    battleEventsByKey.TryAdd(key, battleEvent);
            }

            return battleEventsByKey;
        }

        private Dictionary<string, string> LoadArmourAudioTypesByVariantMeshName()
        {
            var containers = _packFileService.GetAllPackfileContainers();
            var variants = _dbTableQueryService.LoadTables(VariantsTableName, containers).SelectMany(x => x.Rows);
            var unitVariants = _dbTableQueryService.LoadTables(UnitVariantsTableName, containers).SelectMany(x => x.Rows);
            var landUnits = _dbTableQueryService.LoadTables(LandUnitsTableName, containers).SelectMany(x => x.Rows);
            var unitArmourTypes = _dbTableQueryService.LoadTables(UnitArmourTypesTableName, containers).SelectMany(x => x.Rows);

            var variantMeshNamesByVariant = variants
                .Where(x => string.IsNullOrWhiteSpace(x.GetString("variant_name")) == false)
                .GroupBy(x => x.GetString("variant_name")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => NormaliseVariantMeshName(x.First().GetString("variant_filename")),
                    StringComparer.OrdinalIgnoreCase);

            var armourKeysByLandUnit = landUnits
                .Where(x => string.IsNullOrWhiteSpace(x.GetString("key")) == false)
                .GroupBy(x => x.GetString("key")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.First().GetString("armour") ?? "",
                    StringComparer.OrdinalIgnoreCase);

            var audioTypesByArmourKey = unitArmourTypes
                .Where(x => string.IsNullOrWhiteSpace(x.GetString("key")) == false)
                .GroupBy(x => x.GetString("key")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.First().GetString("audio_type") ?? "",
                    StringComparer.OrdinalIgnoreCase);

            var audioTypesByVariantMeshName = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var unitVariant in unitVariants)
            {
                var variant = unitVariant.GetString("variant");
                var landUnit = unitVariant.GetString("unit");
                if (string.IsNullOrWhiteSpace(variant) || string.IsNullOrWhiteSpace(landUnit))
                    continue;
                if (!variantMeshNamesByVariant.TryGetValue(variant, out var variantMeshName) || string.IsNullOrWhiteSpace(variantMeshName))
                    continue;
                if (!armourKeysByLandUnit.TryGetValue(landUnit, out var armourKey) || string.IsNullOrWhiteSpace(armourKey))
                    continue;
                if (!audioTypesByArmourKey.TryGetValue(armourKey, out var audioType) || string.IsNullOrWhiteSpace(audioType))
                    continue;

                if (!audioTypesByVariantMeshName.TryGetValue(variantMeshName, out var audioTypes))
                {
                    audioTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    audioTypesByVariantMeshName.Add(variantMeshName, audioTypes);
                }

                audioTypes.Add(audioType);
            }

            return audioTypesByVariantMeshName.ToDictionary(
                x => x.Key,
                x => string.Join(", ", x.Value.OrderBy(value => value)),
                StringComparer.OrdinalIgnoreCase);
        }

        private static string NormaliseVariantMeshName(string? variantMeshFilename)
        {
            if (string.IsNullOrWhiteSpace(variantMeshFilename))
                return "";

            var filename = Path.GetFileName(variantMeshFilename.Replace('\\', '/'));
            return Path.GetFileNameWithoutExtension(filename);
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
            _variantMeshName = NormaliseVariantMeshName(debugDataToLoad.Mesh.Name);
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
