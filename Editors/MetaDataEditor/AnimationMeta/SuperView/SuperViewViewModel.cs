using CommunityToolkit.Mvvm.ComponentModel;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView
{
    public partial class SuperViewViewModel : EditorHostBase, ISaveableEditor
    {
        SceneObjectViewModel _asset;

        private readonly SceneObjectEditor _sceneObjectBuilder;
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly IMetaDataBuilder _metaDataFactory;
        private readonly IPackFileService _packFileService;
        private readonly IEventHub _eventHub;
        private readonly IUiCommandFactory _uiCommandFactory;
        private PackFile? _animationAudioOwner;
        private string _animationAudioVariantMeshName = "";
        private GameTypeEnum? _animationAudioGame;

        [ObservableProperty] string _persistentMetaFilePath = "";
        [ObservableProperty] string _metaFilePath = "";
        [ObservableProperty] MetaDataEditorViewModel _persistentMetaEditor;
        [ObservableProperty] MetaDataEditorViewModel _metaEditor;
        [ObservableProperty] int _selectedTabControllerIndex = 0;
        public override Type EditorViewModelType => typeof(EditorView);
        public bool HasUnsavedChanges
        {
            get
            {
                return PersistentMetaEditor.HasUnsavedChanges || MetaEditor.HasUnsavedChanges;
            }
            set
            {
                PersistentMetaEditor.HasUnsavedChanges = value;
                MetaEditor.HasUnsavedChanges = value;
            } 
        }


        public SuperViewViewModel(
            IPackFileService packFileService,
            IEventHub eventHub,
            IUiCommandFactory uiCommandFactory,
            SceneObjectEditor sceneObjectBuilder,
            IEditorHostParameters editorHostParameters,
            MetaDataFileParser metaDataFileParser,
            IMetaDataBuilder metaDataFactory)
            : base(editorHostParameters)
        {
            DisplayName = "Super View";
            _packFileService = packFileService;
            _eventHub = eventHub;
            _uiCommandFactory = uiCommandFactory;
            _sceneObjectBuilder = sceneObjectBuilder;
            _metaDataFileParser = metaDataFileParser;
            _metaDataFactory = metaDataFactory;
            audioRepository.Load([Wh3LanguageInformation.GetLanguageAsString(Wh3Language.EnglishUK)]);

            // The engine outlives this editor and is shared with the others, so it is told which
            // banks to resolve events against and which emitter the preview is, rather than
            // working either out for itself.
            _soundEngine.LoadHierarchy(hierarchyProvider);
            _previewedUnit = _soundEngine.RegisterGameObject("Super View preview");
            Initialize();
            eventHub.Register<ScopedFileSavedEvent>(this, OnFileSaved);
            eventHub.Register<SceneObjectUpdateEvent>(this, OnSceneObjectUpdated);
            eventHub.Register<MetaDataAttributeChangedEvent>(this, OnMetaDataAttributeChanged);
            eventHub.Register<SelecteMetaDataAttributeChangedEvent>(this, OnSelectedMetaDataAttributeChanged);
        }

        private void OnSelectedMetaDataAttributeChanged(SelecteMetaDataAttributeChangedEvent @event) => RecreateMetaDataInformation();
        void OnMetaDataAttributeChanged(MetaDataAttributeChangedEvent @event) => RecreateMetaDataInformation();
        void OnMetaDataChanged(SceneObject sceneObject) => RecreateMetaDataInformation();

        private void OnFileSaved(ScopedFileSavedEvent evnt)
        {
            var newFile = _packFileService.FindFile(evnt.NewPath);
            if (evnt.FileOwner == PersistentMetaEditor)
                _sceneObjectBuilder.SetMetaFile(_asset.Data, _asset.Data.MetaData, newFile);
            else if (evnt.FileOwner == MetaEditor)
                _sceneObjectBuilder.SetMetaFile(_asset.Data, newFile, _asset.Data.PersistMetaData);
            else
                throw new Exception($"Unable to determine file owner when reciving a file save event in SuperView. Owner:{evnt.FileOwner}, File:{evnt.NewPath}");
        }

        void Initialize()
        {
            PersistentMetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            MetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            
            var assetViewModel = _sceneObjectViewModelBuilder.CreateAsset("SuperViewRoot", true, "Root", Color.Black,null);
            SceneObjects.Add(assetViewModel);

            assetViewModel.Data.MetaDataChanged += OnMetaDataChanged;
            assetViewModel.Data.AnimationChanged += OnAnimationChanged;

            _asset = assetViewModel;
            _audioTimeline = new AnimationAudioTimeline(_soundEngine, _asset.Data, _previewedUnit);
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

            RecreateMetaDataInformation();
            EnsureAnimationAudioCacheIsCurrent();
            SynchroniseAudioTimeline();
        }

        public void Load(AnimationToolInput debugDataToLoad)
        {
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
            _soundEngine.UnregisterGameObject(_previewedUnit);
            _eventHub?.UnRegister(this);
            base.Close();
        }

        public bool Save()
        {
            var res0 = PersistentMetaEditor.Save();
            var res1 = MetaEditor.Save();
            return res0 && res1;
        }
    }
}
