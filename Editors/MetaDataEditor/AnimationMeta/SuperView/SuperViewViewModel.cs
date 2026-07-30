using CommunityToolkit.Mvvm.ComponentModel;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Audio.Shared.Utilities;
using Editors.Audio.Shared.Wwise;
using Editors.Audio.Shared.Wwise.HircExploration;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using Microsoft.Xna.Framework;
using NAudio.Wave;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.DB;

namespace Editors.AnimationMeta.SuperView
{
    public partial class SuperViewViewModel : EditorHostBase, ISaveableEditor
    {
        private const int AudioTabIndex = 2;

        private readonly ILogger _logger = Logging.Create<SuperViewViewModel>();
        SceneObjectViewModel _asset;

        private readonly SceneObjectEditor _sceneObjectBuilder;
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly IMetaDataBuilder _metaDataFactory;
        private readonly IPackFileService _packFileService;
        private readonly IDbTableQueryService _dbTableQueryService;
        private readonly IActionEventSwitchGroupResolver _actionEventSwitchGroupResolver;
        private readonly IUnitAudioSwitchResolver _unitAudioSwitchResolver;
        private readonly IActionEventAudioResolver _actionEventAudioResolver;
        private readonly ISoundEngine _soundEngine;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly IEventHub _eventHub;
        private readonly IUiCommandFactory _uiCommandFactory;
        private Dictionary<string, string>? _battleEventsByAudioMetadataTagKey;
        private string _variantMeshName = "";
        private readonly HashSet<SoundTrigger_v10> _playedSoundTriggers = [];
        private float _previousAnimationTime = -float.Epsilon;

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
            IDbTableQueryService dbTableQueryService,
            IActionEventSwitchGroupResolver actionEventSwitchGroupResolver,
            IUnitAudioSwitchResolver unitAudioSwitchResolver,
            IActionEventAudioResolver actionEventAudioResolver,
            ISoundEngine soundEngine,
            ApplicationSettingsService applicationSettingsService)
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
            _actionEventSwitchGroupResolver = actionEventSwitchGroupResolver;
            _unitAudioSwitchResolver = unitAudioSwitchResolver;
            _actionEventAudioResolver = actionEventAudioResolver;
            _soundEngine = soundEngine;
            _applicationSettingsService = applicationSettingsService;
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
                var soundEventVariable = AudioMetaEditor.SelectedTag?.Variables
                    .FirstOrDefault(x => x.FieldName == "Sound Event");
                if (soundEventVariable != null)
                {
                    var battleEventDisplay = battleEvent ?? "not found";
                    var relatedValues = new List<string> { $"Battle sound event: {battleEventDisplay}" };

                    if (battleEvent != null)
                    {
                        var switchGroups = _actionEventSwitchGroupResolver.GetSwitchGroups(battleEvent)
                            .Select(x => x.Name)
                            .ToArray();

                        if (switchGroups.Length == 0)
                            relatedValues.Add("Switch group: not found");
                        else
                        {
                            var currentGame = _applicationSettingsService.CurrentSettings.CurrentGame;
                            var switchValues = _unitAudioSwitchResolver.Resolve(currentGame, _variantMeshName, switchGroups);
                            relatedValues.AddRange(switchGroups.Select(group => $"{group}: {(switchValues.TryGetValue(group, out var value) ? value : "not found")}"));
                        }
                    }
                    else
                        relatedValues.Add("Switch group: not found");

                    soundEventVariable.RelatedValue = string.Join("\n", relatedValues);
                }
            }
            catch (Exception exception)
            {
                _logger.Here().Warning(exception, $"Unable to resolve related audio information for sound event '{soundTrigger.SoundEvent}'");
            }
        }

        private Dictionary<string, string> LoadBattleEventsByAudioMetadataTagKey()
        {
            var battleEventsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tables = _dbTableQueryService.LoadTables("audio_metadata_tags_tables", _packFileService.GetAllPackfileContainers());

            foreach (var row in tables.SelectMany(x => x.Rows))
            {
                var key = row.GetString("key");
                var battleEvent = row.GetString("sound_event_battle_start");
                if (!string.IsNullOrWhiteSpace(battleEvent) && !string.IsNullOrWhiteSpace(key))
                    battleEventsByKey.TryAdd(key, battleEvent);
            }

            return battleEventsByKey;
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
            _asset.Data.Player.OnFrameChanged += OnAnimationFrameChanged;
            _asset.Data.Player.OnPlaybackChanged += OnAnimationPlaybackChanged;
            OnSceneObjectUpdated(new SceneObjectUpdateEvent(_asset.Data, false, false, false, true));
        }

        private void OnAnimationPlaybackChanged(bool isPlaying)
        {
            if (isPlaying && _soundEngine.PlaybackState == PlaybackState.Paused)
                _soundEngine.PlayPause();
            else if (!isPlaying && _soundEngine.PlaybackState == PlaybackState.Playing)
                _soundEngine.PlayPause();
        }

        private void OnAnimationFrameChanged(int currentFrame)
        {
            var animationTime = _asset.Data.Player.GetTimeUs() / 1_000_000f;
            if (animationTime < _previousAnimationTime)
            {
                _playedSoundTriggers.Clear();
                _soundEngine.Stop();
                _previousAnimationTime = -float.Epsilon;
            }

            if (!_asset.Data.Player.IsPlaying)
            {
                _previousAnimationTime = animationTime;
                return;
            }

            var soundTriggers = AudioMetaEditor.ParsedFile?.GetItemsOfType<SoundTrigger_v10>() ?? [];
            foreach (var soundTrigger in soundTriggers)
            {
                if (_playedSoundTriggers.Contains(soundTrigger) ||
                    soundTrigger.StartTime < _previousAnimationTime ||
                    soundTrigger.StartTime > animationTime)
                    continue;

                PlaySoundTrigger(soundTrigger);
                _playedSoundTriggers.Add(soundTrigger);
            }

            _previousAnimationTime = animationTime;
        }

        private void PlaySoundTrigger(SoundTrigger_v10 soundTrigger)
        {
            try
            {
                _battleEventsByAudioMetadataTagKey ??= LoadBattleEventsByAudioMetadataTagKey();
                if (!_battleEventsByAudioMetadataTagKey.TryGetValue(soundTrigger.SoundEvent, out var actionEvent))
                {
                    _logger.Here().Warning($"SuperView audio: metadata sound event '{soundTrigger.SoundEvent}' has no battle action-event mapping");
                    return;
                }

                var switchGroups = _actionEventSwitchGroupResolver.GetSwitchGroups(actionEvent)
                    .Select(x => x.Name)
                    .ToArray();
                var currentGame = _applicationSettingsService.CurrentSettings.CurrentGame;
                var switchValues = _unitAudioSwitchResolver.Resolve(currentGame, _variantMeshName, switchGroups);
                var switchValueText = string.Join(", ", switchValues.Select(x => $"{x.Key}={x.Value}"));
                _logger.Here().Information($"SuperView audio: triggering metadata event '{soundTrigger.SoundEvent}' at {soundTrigger.StartTime:0.###}s; action event '{actionEvent}'; switches [{switchValueText}]");
                var wemBytes = _actionEventAudioResolver.ResolveFirstSound(actionEvent, switchValues);
                if (wemBytes == null)
                    return;

                _soundEngine.LoadFromWemBytes(wemBytes);
                _soundEngine.PlayPause();
            }
            catch (Exception exception)
            {
                _logger.Here().Warning(exception, $"Unable to play sound event '{soundTrigger.SoundEvent}' at {soundTrigger.StartTime} seconds");
            }
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
            _playedSoundTriggers.Clear();
            _previousAnimationTime = -float.Epsilon;
            _soundEngine.Stop();
            PersistentMetaEditor.LoadFile(e.Owner.PersistMetaData);
            MetaEditor.LoadFile(e.Owner.MetaData);
            AudioMetaEditor.LoadFile(e.Owner.AudioMetaData);

            RecreateMetaDataInformation();
        }

        public void Load(AnimationToolInput debugDataToLoad)
        {
            _variantMeshName = UnitAudioSwitchResolver.NormaliseVariantMeshName(debugDataToLoad.Mesh.Name);
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
            _asset.Data.Player.OnFrameChanged -= OnAnimationFrameChanged;
            _asset.Data.Player.OnPlaybackChanged -= OnAnimationPlaybackChanged;
            _soundEngine.Dispose();
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
