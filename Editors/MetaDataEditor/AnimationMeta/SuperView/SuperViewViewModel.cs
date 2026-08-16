using CommunityToolkit.Mvvm.ComponentModel;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Utilities;
using Editors.Audio.Shared.Wwise.Engine;
using Editors.Audio.Shared.Wwise.Engine.Cache;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
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
        private readonly ActionEventResolver _actionEventResolver;
        private readonly UnitAudioSwitchResolver _unitAudioSwitchResolver;
        private readonly ISoundEngine _soundEngine;
        private readonly SoundEngineCache _soundEngineCache;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly IEventHub _eventHub;
        private readonly IUiCommandFactory _uiCommandFactory;
        private Dictionary<string, string>? _battleEventsByAudioMetadataTagKey;
        private Dictionary<string, AnimationAudioResolution> _animationAudioBySoundEvent = new(StringComparer.OrdinalIgnoreCase);
        private AnimationAudioTimeline _audioTimeline;
        private string _variantMeshName = "";
        private PackFile? _animationAudioOwner;
        private string _animationAudioVariantMeshName = "";
        private GameTypeEnum? _animationAudioGame;

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
            IAudioRepository audioRepository,
            ActionEventResolver actionEventResolver,
            UnitAudioSwitchResolver unitAudioSwitchResolver,
            ISoundEngine soundEngine,
            SoundEngineCache soundEngineCache,
            ApplicationSettingsService applicationSettingsService)
            : base(editorHostParameters)
        {
            DisplayName = "Super View";
            _packFileService = packFileService;
            _eventHub = eventHub;
            _uiCommandFactory = uiCommandFactory;
            _sceneObjectBuilder = sceneObjectBuilder;
            _metaDataFileParser = metaDataFileParser;
            _metaDataFactory = metaDataFactory;
            _dbTableQueryService = dbTableQueryService;
            _actionEventResolver = actionEventResolver;
            _unitAudioSwitchResolver = unitAudioSwitchResolver;
            _soundEngine = soundEngine;
            _soundEngineCache = soundEngineCache;
            _applicationSettingsService = applicationSettingsService;
            audioRepository.Load([Wh3LanguageInformation.GetLanguageAsString(Wh3Language.EnglishUK)]);
            Initialize();
            eventHub.Register<ScopedFileSavedEvent>(this, OnFileSaved);
            eventHub.Register<SceneObjectUpdateEvent>(this, OnSceneObjectUpdated);
            eventHub.Register<MetaDataAttributeChangedEvent>(this, OnMetaDataAttributeChanged);
            eventHub.Register<SelecteMetaDataAttributeChangedEvent>(this, OnSelectedMetaDataAttributeChanged);
        }

        private void OnSelectedMetaDataAttributeChanged(SelecteMetaDataAttributeChangedEvent @event)
            => RefreshAfterMetaDataEdit();

        void OnMetaDataAttributeChanged(MetaDataAttributeChangedEvent @event)
            => RefreshAfterMetaDataEdit();

        private void RefreshAfterMetaDataEdit()
        {
            RecreateMetaDataInformation();
            EnsureAnimationAudioCacheIsCurrent();
            SynchroniseAudioTimeline();
            UpdateAudioSoundEventDatabaseKey();
        }
        void OnMetaDataChanged(SceneObject sceneObject)
        {
            RecreateMetaDataInformation();
            EnsureAnimationAudioCacheIsCurrent();
            SynchroniseAudioTimeline();
        }
        void OnAnimationChanged(AnimationClip _) => SynchroniseAudioTimeline();

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
                EnsureAnimationAudioCacheIsCurrent();
                _animationAudioBySoundEvent.TryGetValue(soundTrigger.SoundEvent, out var animationAudio);
                var soundEventVariable = AudioMetaEditor.SelectedTag?.Variables
                    .FirstOrDefault(variable => variable.FieldName == "Sound Event");
                if (soundEventVariable != null)
                {
                    var battleEventDisplay = animationAudio?.ActionEvent ?? "not found";
                    var relatedValues = new List<string> { $"Battle sound event: {battleEventDisplay}" };

                    if (animationAudio?.ActionEvent != null)
                    {
                        if (animationAudio.SwitchGroups.Count == 0)
                            relatedValues.Add("Switch group: not found");
                        else
                        {
                            relatedValues.AddRange(animationAudio.SwitchGroups.Select(switchGroupName =>
                                $"{switchGroupName}: {(animationAudio.SwitchValues.TryGetValue(switchGroupName, out var switchValue) ? switchValue : "not found")}"));
                        }
                    }
                    else
                        relatedValues.Add("Switch group: not found");

                    var wemDisplay = animationAudio?.WemId is uint wemId
                        ? animationAudio.IsWemDidx
                            ? $"{wemId}.wem embedded in {animationAudio.WemFilePath}"
                            : animationAudio.WemFilePath
                        : "not found";
                    relatedValues.Add($"WEM: {wemDisplay}");
                    soundEventVariable.RelatedValue = string.Join("\n", relatedValues);
                }
            }
            catch (Exception exception)
            {
                _logger.Here().Warning(exception, "Unable to resolve related audio information for sound event '{SoundEvent}'", soundTrigger.SoundEvent);
            }
        }

        private Dictionary<string, string> LoadBattleEventsByAudioMetadataTagKey()
        {
            var battleEventsByAudioMetadataTagKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var audioMetadataTagTables = _dbTableQueryService.LoadTables("audio_metadata_tags_tables", _packFileService.GetAllPackfileContainers());

            foreach (var audioMetadataTagRow in audioMetadataTagTables.SelectMany(table => table.Rows))
            {
                var audioMetadataTagKey = audioMetadataTagRow.GetString("key");
                var battleActionEventName = audioMetadataTagRow.GetString("sound_event_battle_start");
                if (!string.IsNullOrWhiteSpace(battleActionEventName) && !string.IsNullOrWhiteSpace(audioMetadataTagKey))
                    battleEventsByAudioMetadataTagKey.TryAdd(audioMetadataTagKey, battleActionEventName);
            }

            return battleEventsByAudioMetadataTagKey;
        }

        private void EnsureAnimationAudioCacheIsCurrent()
        {
            var audioMetaFile = AudioMetaEditor.CurrentFile;
            var parsedAudioMeta = AudioMetaEditor.ParsedFile;
            var game = _applicationSettingsService.CurrentSettings.CurrentGame;
            var soundEvents = (parsedAudioMeta?.GetItemsOfType<SoundTrigger_v10>() ?? [])
                .Select(soundTrigger => soundTrigger.SoundEvent)
                .Where(soundEvent => !string.IsNullOrWhiteSpace(soundEvent))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var isCurrent = _animationAudioOwner != null
                && ReferenceEquals(_animationAudioOwner, audioMetaFile)
                && string.Equals(_animationAudioVariantMeshName, _variantMeshName, StringComparison.OrdinalIgnoreCase)
                && _animationAudioGame == game
                && soundEvents.SetEquals(_animationAudioBySoundEvent.Keys);
            if (isCurrent)
                return;

            ClearAnimationAudioCache();
            if (audioMetaFile == null
                || parsedAudioMeta == null
                || string.IsNullOrWhiteSpace(_variantMeshName))
                return;

            try
            {
                _animationAudioBySoundEvent = ResolveAnimationAudio(soundEvents, game, _variantMeshName);

                _animationAudioOwner = audioMetaFile;
                _animationAudioVariantMeshName = _variantMeshName;
                _animationAudioGame = game;
                var playableAudioCount = _animationAudioBySoundEvent.Values.Count(animationAudio => animationAudio.Audio != null);
                _logger.Here().Information(
                    $"Prepared animation audio for variant mesh '{_variantMeshName}': {_animationAudioBySoundEvent.Count} sound events, {playableAudioCount} with playable audio");
            }
            catch (Exception exception)
            {
                _logger.Here().Error(exception, $"Unable to prepare animation audio for variant mesh '{_variantMeshName}'");
            }
        }

        private Dictionary<string, AnimationAudioResolution> ResolveAnimationAudio(
            HashSet<string> soundEvents,
            GameTypeEnum game,
            string variantMeshName)
        {
            _battleEventsByAudioMetadataTagKey ??= LoadBattleEventsByAudioMetadataTagKey();

            var actionEventNameBySoundEventName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var soundEventName in soundEvents)
            {
                if (_battleEventsByAudioMetadataTagKey.TryGetValue(soundEventName, out var actionEventName))
                    actionEventNameBySoundEventName[soundEventName] = actionEventName;
                else
                    _logger.Here().Warning($"Sound event '{soundEventName}' has no battle action event mapping, so it cannot be played");
            }

            var switchGroupNamesByActionEventName = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var actionEventName in actionEventNameBySoundEventName.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                switchGroupNamesByActionEventName[actionEventName] = _actionEventResolver
                    .GetSwitchGroups(actionEventName)
                    .Select(switchGroup => switchGroup.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            var allSwitchGroupNames = switchGroupNamesByActionEventName.Values
                .SelectMany(switchGroupNames => switchGroupNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var resolvedSwitchValuesByGroupName = _unitAudioSwitchResolver.Resolve(game, variantMeshName, allSwitchGroupNames);

            var animationAudioByActionEventName = new Dictionary<string, AnimationAudioResolution>(StringComparer.OrdinalIgnoreCase);
            foreach (var (actionEventName, switchGroupNames) in switchGroupNamesByActionEventName)
            {
                var switchValuesByGroupName = switchGroupNames
                    .Where(resolvedSwitchValuesByGroupName.ContainsKey)
                    .ToDictionary(switchGroupName => switchGroupName, switchGroupName => resolvedSwitchValuesByGroupName[switchGroupName], StringComparer.OrdinalIgnoreCase);
                var resolvedWem = _actionEventResolver.ResolveFirstSound(actionEventName, switchValuesByGroupName);
                var audio = resolvedWem != null
                    ? _soundEngineCache.GetWem(resolvedWem.Data)
                    : null;
                animationAudioByActionEventName[actionEventName] = new AnimationAudioResolution(
                    actionEventName,
                    switchGroupNames,
                    switchValuesByGroupName,
                    resolvedWem?.Id,
                    resolvedWem?.FilePath,
                    resolvedWem?.IsDidx ?? false,
                    audio);
            }

            return soundEvents.ToDictionary(
                soundEventName => soundEventName,
                soundEventName => actionEventNameBySoundEventName.TryGetValue(soundEventName, out var actionEventName)
                    ? animationAudioByActionEventName[actionEventName]
                    : new AnimationAudioResolution(null, [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null, null, false, null),
                StringComparer.OrdinalIgnoreCase);
        }

        private void ClearAnimationAudioCache()
        {
            _audioTimeline?.StopAndRelease();
            _animationAudioBySoundEvent.Clear();
            _animationAudioOwner = null;
            _animationAudioVariantMeshName = "";
            _animationAudioGame = null;
            _battleEventsByAudioMetadataTagKey = null;
        }

        private void SynchroniseAudioTimeline()
        {
            if (_audioTimeline == null)
                return;

            var soundTriggers = AudioMetaEditor.ParsedFile?.GetItemsOfType<SoundTrigger_v10>() ?? [];
            var audioCues = soundTriggers
                .Select(soundTrigger => (
                    soundTrigger.SoundEvent,
                    soundTrigger.StartTime,
                    _animationAudioBySoundEvent.GetValueOrDefault(soundTrigger.SoundEvent)?.Audio))
                .Where(triggeredAudio => triggeredAudio.Audio != null)
                .Select(triggeredAudio => new AnimationAudioTimeline.AudioCue(
                    triggeredAudio.SoundEvent,
                    TimeSpan.FromSeconds(triggeredAudio.StartTime),
                    triggeredAudio.Audio!))
                .ToArray();

            _audioTimeline.Synchronise(audioCues);
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

            var assetViewModel = _sceneObjectViewModelBuilder.CreateAsset("SuperViewRoot", true, "Root", Color.Black, null);
            SceneObjects.Add(assetViewModel);

            assetViewModel.Data.MetaDataChanged += OnMetaDataChanged;
            assetViewModel.Data.AnimationChanged += OnAnimationChanged;

            _asset = assetViewModel;
            _audioTimeline = new AnimationAudioTimeline(_soundEngine, _asset.Data);
            _asset.Data.Player.OnFrameChanged += OnAnimationFrameChanged;
            _asset.Data.Player.OnPlaybackChanged += OnAnimationPlaybackChanged;
            OnSceneObjectUpdated(new SceneObjectUpdateEvent(_asset.Data, false, false, false, true));
        }

        private void OnAnimationPlaybackChanged(bool isPlaying)
        {
            // Another editor may have taken the sound engine over since this timeline was
            // built, in which case it has to be rebuilt before it can be started.
            if (isPlaying && !_audioTimeline.IsCurrent)
            {
                _audioTimeline.Release();
                SynchroniseAudioTimeline();
                return;
            }

            _audioTimeline.OnAnimationPlaybackChanged(isPlaying);
        }

        private void OnAnimationFrameChanged(int currentFrame)
        {
            // Looping and animation length are inputs to the scheduled timeline that nothing
            // raises an event for, so a stale timeline is noticed here rather than left until
            // some unrelated edit happens to rebuild it.
            if (_audioTimeline.NeedsResynchronisation)
                SynchroniseAudioTimeline();

            _audioTimeline.OnAnimationFrameChanged();
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
            if (!ReferenceEquals(AudioMetaEditor.CurrentFile, e.Owner.AudioMetaData))
                ClearAnimationAudioCache();
            PersistentMetaEditor.LoadFile(e.Owner.PersistMetaData);
            MetaEditor.LoadFile(e.Owner.MetaData);
            AudioMetaEditor.LoadFile(e.Owner.AudioMetaData);

            RecreateMetaDataInformation();
            EnsureAnimationAudioCacheIsCurrent();
            SynchroniseAudioTimeline();
        }

        public void Load(AnimationToolInput debugDataToLoad)
        {
            var variantMeshName = VariantMeshName.Normalise(debugDataToLoad.Mesh.Name);
            if (!string.Equals(_variantMeshName, variantMeshName, StringComparison.OrdinalIgnoreCase))
                ClearAnimationAudioCache();
            _variantMeshName = variantMeshName;
            _sceneObjectBuilder.SetMesh(_asset.Data, debugDataToLoad.Mesh);
            EnsureAnimationAudioCacheIsCurrent();

            // Hack :(
            if (debugDataToLoad.AnimationSlot != null)
            {
                var frag = _asset.FragAndSlotSelection.FragmentList.PossibleValues.FirstOrDefault(x => x.FullPath == debugDataToLoad.FragmentName);
                _asset.FragAndSlotSelection.FragmentList.SelectedItem = frag;

                var slot = _asset.FragAndSlotSelection.FragmentSlotList.PossibleValues.First(x => x.SlotName == debugDataToLoad.AnimationSlot.Value);
                _asset.FragAndSlotSelection.FragmentSlotList.SelectedItem = slot;
            }
            SynchroniseAudioTimeline();
        }


        public void RefreshAction() => _asset.Data.TriggerMeshChanged();

        public override void Close()
        {
            _asset.Data.Player.OnFrameChanged -= OnAnimationFrameChanged;
            _asset.Data.Player.OnPlaybackChanged -= OnAnimationPlaybackChanged;
            _asset.Data.AnimationChanged -= OnAnimationChanged;
            _asset.Data.MetaDataChanged -= OnMetaDataChanged;
            ClearAnimationAudioCache();
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
