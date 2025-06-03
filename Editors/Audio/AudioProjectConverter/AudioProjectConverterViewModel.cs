﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor;
using Editors.Audio.AudioEditor.AudioProjectViewer;
using Editors.Audio.AudioEditor.AudioSettings;
using Editors.Audio.Storage;
using Editors.Audio.Utility;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.AudioProjectConverter
{
    public class WavFile
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public uint WemId { get; set; }
        public string DialogueEvent { get; set; }
    }

    public class StatePathInfo
    {
        public string JoinedStatePath { get; set; }
        public List<StatePathNode> StatePathNodes { get; set; }
        public List<WavFile> WavFiles { get; set; }
    }

    public partial class AudioProjectConverterViewModel : ObservableObject
    {
        private readonly IStandardDialogs _standardDialogs;
        private readonly IFileSaveService _fileSaveService;
        private readonly IAudioRepository _audioRepository;
        private readonly IAudioEditorService _audioEditorService;
        private readonly BnkParser _bnkParser;
        private readonly VgStreamWrapper _vgStreamWrapper;

        private readonly ILogger _logger = Logging.Create<AudioProjectViewerViewModel>();

        private System.Action _closeAction;

        // Settings properties
        [ObservableProperty] private string _audioProjectName;
        [ObservableProperty] private string _outputDirectoryPath;
        [ObservableProperty] private string _wemsDirectoryPath;
        [ObservableProperty] private string _bnksDirectoryPath;
        [ObservableProperty] private string _vOActorSubstring;
        [ObservableProperty] private string _soundbanksInfoXmlPath;
        [ObservableProperty] private bool _isUsingWwiseProject;

        // Ok button enablement
        [ObservableProperty] private bool _isAudioProjectNameSet;
        [ObservableProperty] private bool _isOutputDirectoryPathSet;
        [ObservableProperty] private bool _isWemsDirectoryPathSet; 
        [ObservableProperty] private bool _isBnksDirectoryPathSet;
        [ObservableProperty] private bool _isVOActorSubstringSet;
        [ObservableProperty] private bool _isSoundbanksInfoXmlSet;
        [ObservableProperty] private bool _isOkButtonEnabled;

        public AudioProjectConverterViewModel(
            IStandardDialogs standardDialogs,
            IFileSaveService fileSaveService,
            IAudioRepository audioRepository,
            IAudioEditorService audioEditorService,
            BnkParser bnkParser,
            VgStreamWrapper vgStreamWrapper)
        {
            _standardDialogs = standardDialogs;
            _fileSaveService = fileSaveService;
            _audioRepository = audioRepository;
            _audioEditorService = audioEditorService;
            _bnkParser = bnkParser;
            _vgStreamWrapper = vgStreamWrapper;

            OutputDirectoryPath = "audio\\audio_projects";
        }

        [RelayCommand] public void ProcessAudioProjectConversion()
        {
            var audioProject = AudioProject.CreateAudioProject();

            var soundBankPaths = new List<string>();
            if (Directory.Exists(BnksDirectoryPath))
                soundBankPaths = Directory.GetFiles(BnksDirectoryPath, "*.bnk", SearchOption.TopDirectoryOnly).ToList();

            var statesLookupByStateGroupByStateId = new Dictionary<string, Dictionary<uint, string>>();
            var dialogueEventsLookupByWemId = new Dictionary<uint, List<string>>();
            var statePathsLookupByDialogueEvent = new Dictionary<string, List<StatePathInfo>>();
            var dialogueEventsToProcess = new List<ICAkDialogueEvent>();
            var moddedStateGroups = new Dictionary<string, List<string>>();
            var processedWems = new Dictionary<uint, AudioFile>();
            var globalBaseNameUsage = new Dictionary<string, int>();

            var hircItems = GetHircItems(soundBankPaths);
            var hircLookupById = BuildHircLookupById(hircItems);

            if (!string.IsNullOrEmpty(SoundbanksInfoXmlPath))
                BuildStateGroupStateLookup(statesLookupByStateGroupByStateId);

            var dialogueEvents = hircItems.OfType<ICAkDialogueEvent>().ToList();
            foreach (var dialogueEvent in dialogueEvents)
                PrepareDialogueEventProcessing(dialogueEvent, dialogueEventsLookupByWemId, statePathsLookupByDialogueEvent, statesLookupByStateGroupByStateId, hircLookupById, dialogueEventsToProcess, moddedStateGroups);

            foreach (var dialogueEvent in dialogueEventsToProcess)
                ProcessDialogueEvent(audioProject, dialogueEvent, processedWems, dialogueEventsLookupByWemId, statePathsLookupByDialogueEvent, globalBaseNameUsage);

            ProcessModdedStateGroups(audioProject, moddedStateGroups);

            audioProject = AudioProject.GetAudioProject(audioProject);
            _audioEditorService.SaveAudioProject(audioProject, AudioProjectName, OutputDirectoryPath);

            CloseWindowAction();
        }

        private static Dictionary<uint, List<HircItem>> BuildHircLookupById(List<HircItem> hircItems)
        {
            var hircLookupById = new Dictionary<uint, List<HircItem>>();
            foreach (var hircItem in hircItems)
            {
                if (!hircLookupById.TryGetValue(hircItem.Id, out var hircItemList))
                {
                    hircItemList = [];
                    hircLookupById.TryAdd(hircItem.Id, hircItemList);
                }
                hircItemList.Add(hircItem);
            }

            return hircLookupById;
        }

        private List<HircItem> GetHircItems(List<string> soundBankPaths)
        {
            var parsedSoundBanks = new List<ParsedBnkFile>();

            foreach (var soundBankPath in soundBankPaths)
            {
                var soundBankDataBytes = File.ReadAllBytes(soundBankPath);
                var soundBankPackFile = PackFile.CreateFromBytes(soundBankPath, soundBankDataBytes);
                var parsedSoundBank = _bnkParser.Parse(soundBankPackFile, soundBankPath, false);
                parsedSoundBanks.Add(parsedSoundBank);
            }

            var hircItems = parsedSoundBanks
                .SelectMany(soundBank => soundBank.HircChunk.HircItems)
                .ToList();

            return hircItems;
        }

        private Dictionary<string, Dictionary<uint, string>> BuildStateGroupStateLookup(Dictionary<string, Dictionary<uint, string>> stateLookupByStateGroup)
        {
            var soundBanksInfo = XDocument.Load(SoundbanksInfoXmlPath);

            foreach (var stateGroup in soundBanksInfo.Descendants("StateGroup"))
            {
                var stateGroupName = stateGroup.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(stateGroupName))
                    continue;

                if (!stateLookupByStateGroup.TryGetValue(stateGroupName, out var stateLookupByStateId))
                {
                    stateLookupByStateId = [];
                    stateLookupByStateGroup.TryAdd(stateGroupName, stateLookupByStateId);
                }

                foreach (var state in stateGroup.Element("States")?.Elements("State") ?? [])
                {
                    var stateName = state.Attribute("Name")?.Value;
                    if (!string.IsNullOrEmpty(stateName))
                    {
                        var stateHash = WwiseHash.Compute(stateName);
                        stateLookupByStateId.TryAdd(stateHash, stateName);
                    }
                }
            }

            return stateLookupByStateGroup;
        }

        private void PrepareDialogueEventProcessing(
            ICAkDialogueEvent dialogueEvent,
            Dictionary<uint, List<string>> dialogueEventsLookupByWemId,
            Dictionary<string, List<StatePathInfo>> statePathsLookupByDialogueEvent,
            Dictionary<string, Dictionary<uint, string>> statesLookupByStateGroupByStateId,
            Dictionary<uint, List<HircItem>> hircLookupById,
            List<ICAkDialogueEvent> dialogueEventsToProcess,
            Dictionary<string, List<string>> moddedStateGroups)
        {
            var stateGroup = "VO_Actor";
            var statesLookupByStateId = GetStatesLookupByStateId(statesLookupByStateGroupByStateId, stateGroup);

            var dialogueEventHirc = dialogueEvent as HircItem;
            var dialogueEventName = _audioRepository.GetNameFromId(dialogueEventHirc.Id);

            var decisionPathHelper = new DecisionPathHelper(_audioRepository);
            var decisionPathCollection = decisionPathHelper.GetDecisionPaths(dialogueEvent);

            var voActorSubstrings = VOActorSubstring
                .Split([','], StringSplitOptions.RemoveEmptyEntries)
                .Select(substring => substring.Trim().ToLower())
                .ToArray();

            var anyStatePathsContainRequiredPattern = decisionPathCollection.Paths
                .Any(statePath => statePath.Items.Any(state =>
                    statesLookupByStateId.TryGetValue(state.Value, out var stateName) &&
                    voActorSubstrings.Any(substring => stateName.Contains(substring, StringComparison.CurrentCultureIgnoreCase))));

            if (!anyStatePathsContainRequiredPattern)
                return;

            // Store the dialogue event for use later
            dialogueEventsToProcess.Add(dialogueEvent);

            foreach (var statePath in decisionPathCollection.Paths)
            {
                var statePathContainsRequiredPattern = statePath.Items.Any(state =>
                    statesLookupByStateId.TryGetValue(state.Value, out var stateName) &&
                    voActorSubstrings.Any(substring => stateName.Contains(substring, StringComparison.CurrentCultureIgnoreCase)));

                if (!statePathContainsRequiredPattern)
                    continue;

                var wavFiles = new List<WavFile>();
                var statePathNodes = new List<StatePathNode>();

                StoreStateGroupAndStateInfo(dialogueEvent, statesLookupByStateId, statesLookupByStateGroupByStateId, statePath, statePathNodes, moddedStateGroups);

                StoreDialogueEventsLookupByWemId(dialogueEventsLookupByWemId, hircLookupById, wavFiles, dialogueEventName, statePath);

                StoreStatePathInfo(statePathsLookupByDialogueEvent, wavFiles, dialogueEventName, statePathNodes);
            }
        }

        private Dictionary<uint, string> GetStatesLookupByStateId(Dictionary<string, Dictionary<uint, string>> statesLookupByStateGroupByStateId, string stateGroupLookup)
        {
            statesLookupByStateGroupByStateId.TryGetValue(stateGroupLookup, out var statesInfoFromWwiseProject);
            _audioRepository.StatesLookupByStateGroupByStateId.TryGetValue(stateGroupLookup, out var statesInfoFromGame);

            var statesLookupByStateId = new Dictionary<uint, string>();

            if (statesInfoFromWwiseProject != null)
            {
                foreach (var stateInfo in statesInfoFromWwiseProject)
                    statesLookupByStateId.TryAdd(stateInfo.Key, stateInfo.Value);
            }

            if (statesInfoFromGame != null)
            {
                foreach (var stateInfo in statesInfoFromGame)
                    statesLookupByStateId.TryAdd(stateInfo.Key, stateInfo.Value);
            }

            return statesLookupByStateId;
        }

        private void StoreStateGroupAndStateInfo(
            ICAkDialogueEvent dialogueEvent,
            Dictionary<uint, string> wwiseStatesIdLookup,
            Dictionary<string, Dictionary<uint, string>> statesLookupByStateGroupByStateId,
            DecisionPathHelper.DecisionPath statePath,
            List<StatePathNode> statePathNodes,
            Dictionary<string, List<string>> moddedStateGroups)
        {
            var stateGroupIndex = 0;

            foreach (var statePathItem in statePath.Items)
            {
                var stateGroup = _audioRepository.GetNameFromId(dialogueEvent.Arguments[stateGroupIndex].GroupId);
                var statesLookupByStateId = GetStatesLookupByStateId(statesLookupByStateGroupByStateId, stateGroup);
                var state = string.Empty;

                if (statePathItem.Value == 0)
                    state = "Any";
                else
                {
                    if (statesLookupByStateId.TryGetValue(statePathItem.Value, out var unhashedState))
                        state = unhashedState;
                }

                statePathNodes.Add(new StatePathNode
                {
                    StateGroup = new StateGroup { Name = stateGroup },
                    State = new State { Name = state }
                });

                // Store modded states info
                if (state != "Any" && !_audioRepository.NameLookupById.ContainsValue(state))
                {
                    if (moddedStateGroups.TryGetValue(stateGroup, out var stateList))
                    {
                        if (!stateList.Contains(state))
                            stateList.Add(state);
                    }
                    else
                        moddedStateGroups.TryAdd(stateGroup, [state]);
                }

                stateGroupIndex++;
            }
        }

        private static void StoreDialogueEventsLookupByWemId(
            Dictionary<uint, List<string>> dialogueEventsLookupByWemId,
            Dictionary<uint, List<HircItem>> hircLookupById,
            List<WavFile> wavFiles,
            string dialogueEventName,
            DecisionPathHelper.DecisionPath statePath)
        {
            ProcessHircItem(statePath.ChildNodeId, dialogueEventsLookupByWemId, hircLookupById, wavFiles, dialogueEventName);
        }

        private static void ProcessHircItem(
            uint childNodeId,
            Dictionary<uint, List<string>> dialogueEventsLookupByWemId,
            Dictionary<uint, List<HircItem>> hircLookupById,
            List<WavFile> wavFiles,
            string dialogueEventName)
        {
            if (!hircLookupById.TryGetValue(childNodeId, out var hircItems) || hircItems.Count == 0)
                return;

            var hircItem = hircItems.First();

            if (hircItem is ICAkSound soundHirc)
            {
                var wavFile = new WavFile()
                {
                    WemId = soundHirc.GetSourceId(),
                    DialogueEvent = dialogueEventName
                };
                wavFiles.Add(wavFile);

                if (dialogueEventsLookupByWemId.TryGetValue(wavFile.WemId, out var dialogueEventList))
                {
                    if (!dialogueEventList.Contains(dialogueEventName))
                        dialogueEventList.Add(dialogueEventName);
                }
                else
                    dialogueEventsLookupByWemId.TryAdd(wavFile.WemId, [dialogueEventName]);
            }
            else if (hircItem is ICAkRanSeqCntr ranSeqCntr)
            {
                foreach (var childId in ranSeqCntr.GetChildren())
                    ProcessHircItem(childId, dialogueEventsLookupByWemId, hircLookupById, wavFiles, dialogueEventName);
            }
        }

        private static void StoreStatePathInfo(Dictionary<string, List<StatePathInfo>> statePathsLookupByDialogueEvent, List<WavFile> wavFiles, string dialogueEventName, List<StatePathNode> statePathNodes)
        {
            var joinedStatePath = string.Join(".", statePathNodes.Select(statePathNode => statePathNode.State.Name));

            var statePathInfo = new StatePathInfo
            {
                JoinedStatePath = joinedStatePath,
                StatePathNodes = statePathNodes,
                WavFiles = wavFiles
            };

            if (statePathsLookupByDialogueEvent.TryGetValue(dialogueEventName, out var statePath))
            {
                var containsJoinedStatePath = statePath.Any(statePathInfo => statePathInfo.JoinedStatePath == joinedStatePath);
                if (!containsJoinedStatePath)
                    statePath.Add(statePathInfo);
            }
            else
                statePathsLookupByDialogueEvent.TryAdd(dialogueEventName, [statePathInfo]);
        }

        private void ProcessDialogueEvent(
            AudioProject audioProject,
            ICAkDialogueEvent dialogueEvent,
            Dictionary<uint, AudioFile> processedWems,
            Dictionary<uint, List<string>> dialogueEventsLookupByWemId,
            Dictionary<string, List<StatePathInfo>> statePathsLookupByDialogueEvent,
            Dictionary<string, int> globalBaseNameUsage)
        {
            var dialogueEventHirc = dialogueEvent as HircItem;
            var dialogueEventName = _audioRepository.GetNameFromId(dialogueEventHirc.Id);

            _logger.Here().Information($"Processing Dialogue Event: {dialogueEventName}");

            var audioProjectDialogueEvent = audioProject.SoundBanks
                .Where(soundBank => soundBank.DialogueEvents != null)
                .SelectMany(soundBank => soundBank.DialogueEvents)
                .FirstOrDefault(dialogueEvent => dialogueEvent.Name == dialogueEventName);

            var voActorSubstrings = VOActorSubstring
                .Split([','], StringSplitOptions.RemoveEmptyEntries)
                .Select(substring => substring.Trim().ToLower())
                .ToArray();

            foreach (var statePath in statePathsLookupByDialogueEvent[dialogueEventName].ToList())
            {
                _logger.Here().Information($"Processing State Path: {statePath.JoinedStatePath}");

                var audioFiles = new List<AudioFile>();
                var statePathWavs = statePath.WavFiles;

                // Get the first VO_Actor as that should? always be the one we want
                var voActor = statePath.StatePathNodes
                    .Where(statePathNode => statePathNode.StateGroup.Name == "VO_Actor")
                    .FirstOrDefault()?.State.Name;

                // Stop if not containing the thing we're after
                if (!voActorSubstrings.Any(substring => voActor.Contains(substring, StringComparison.CurrentCultureIgnoreCase)))
                    continue;

                ProcessWavFiles(statePathWavs, processedWems, audioFiles, dialogueEventsLookupByWemId, globalBaseNameUsage, dialogueEventName, voActor);

                var audioProjectStatePath = AudioProjectHelpers.CreateStatePathFromStatePathNodes(_audioRepository, statePath.StatePathNodes, audioFiles);
                AudioProjectHelpers.InsertStatePathAlphabetically(audioProjectDialogueEvent, audioProjectStatePath);

                // Remove the processed statePath from the original list as if we're processing multiple bnks
                // as Dialogue Events can occur several times but so it would add duplicates of the same state path
                statePathsLookupByDialogueEvent[dialogueEventName].Remove(statePath);
            }
        }

        private void ProcessWavFiles(
            List<WavFile> statePathWavs,
            Dictionary<uint, AudioFile> processedWems,
            List<AudioFile> audioFiles,
            Dictionary<uint, List<string>> dialogueEventsLookupByWemId,
            Dictionary<string, int> globalBaseNameUsage,
            string dialogueEventName,
            string voActor)
        {
            var voActorSegment = voActor.Substring(voActor.IndexOf("vo_actor_") + "vo_actor_".Length).ToLower();

            foreach (var wavFile in statePathWavs)
            {
                // Check for already processed files
                if (processedWems.TryGetValue(wavFile.WemId, out var processedAudioFile))
                {
                    audioFiles.Add(processedAudioFile);
                    continue;
                }

                var chosenDialogueEventName = GetPreferredDialogueEventName(wavFile.WemId, dialogueEventName, dialogueEventsLookupByWemId).ToLower();
                var baseFileName = $"{voActorSegment}_{chosenDialogueEventName}".ToLower();

                // Ensure unique numbering across events globally
                if (!globalBaseNameUsage.TryGetValue(baseFileName, out var count))
                    count = 0;
                count++;
                globalBaseNameUsage[baseFileName] = count;

                wavFile.FileName = $"{baseFileName}_{count}.wav".ToLower();
                wavFile.FilePath = $"audio_projects\\audio\\vo\\{voActorSegment}\\{wavFile.FileName}".ToLower();

                // Create and store the processed AudioFile
                processedAudioFile = new AudioFile
                {
                    FileName = wavFile.FileName,
                    FilePath = wavFile.FilePath
                };
                processedWems.Add(wavFile.WemId, processedAudioFile);
                audioFiles.Add(processedAudioFile);

                var wemFilePath = $"{WemsDirectoryPath}\\{wavFile.WemId}.wem";
                var wavTempFilePath = $"{DirectoryHelper.Temp}\\Audio\\{wavFile.FileName}";
                var wavPackOutputPath = $"{OutputDirectoryPath}\\vo\\{voActorSegment}\\{wavFile.FileName}";

                var conversionResult = _vgStreamWrapper.ConvertFileUsingVgStream(wemFilePath, wavTempFilePath);
                if (conversionResult.Failed)
                    throw new Exception($"Failed to convert {wemFilePath} to {wavTempFilePath}");

                var wavFileBytes = File.ReadAllBytes(wavTempFilePath);
                var packFile = PackFile.CreateFromBytes(AudioProjectName, wavFileBytes);
                _fileSaveService.Save(wavPackOutputPath, packFile.DataSource.ReadData(), true);
            }
        }

        private static void ProcessModdedStateGroups(AudioProject audioProject, Dictionary<string, List<string>> moddedStateGroups)
        {
            foreach (var moddedStateGroup in moddedStateGroups)
            {
                foreach (var moddedState in moddedStateGroup.Value)
                {
                    var audioProjectModdedState = new State
                    {
                        Name = moddedState
                    };
                    var audioProjectStateGroup = audioProject.StateGroups.FirstOrDefault(stateGroup => stateGroup.Name == moddedStateGroup.Key);
                    AudioProjectHelpers.InsertStateAlphabetically(audioProjectStateGroup, audioProjectModdedState);
                }
            }
        }

        private static string GetPreferredDialogueEventName(uint wemId, string fallbackDialogueEventName, Dictionary<uint, List<string>> dialogueEventsLookupByWemId)
        {
            if (dialogueEventsLookupByWemId.TryGetValue(wemId, out var dialogueEvents) && dialogueEvents != null && dialogueEvents.Count > 0)
            {
                // A list of Dialogue Events whose names I want to prioritise as the name of wems if they appear in them
                var priorityList = new List<string>
                {
                    "battle_vo_order_attack",
                    "battle_vo_order_move",
                    "battle_vo_order_select",
                    "battle_vo_order_halt",
                    "battle_vo_order_withdraw",
                    "battle_vo_order_withdraw_tactical",
                    "battle_vo_order_generic_response",
                    "campaign_vo_attack",
                    "campaign_vo_move",
                    "campaign_vo_selected",
                    "campaign_vo_yes",
                    "campaign_vo_no",
                };

                foreach (var priority in priorityList)
                {
                    if (dialogueEvents.Any(dialogueEvent => dialogueEvent.Equals(priority, StringComparison.OrdinalIgnoreCase)))
                        return dialogueEvents.First(dialogueEvent => dialogueEvent.Equals(priority, StringComparison.OrdinalIgnoreCase));
                }

                return dialogueEvents.OrderBy(dialogueEvent => dialogueEvent, StringComparer.OrdinalIgnoreCase).First();
            }
            return fallbackDialogueEventName;
        }

        partial void OnAudioProjectNameChanged(string value)
        {
            IsAudioProjectNameSet = !string.IsNullOrEmpty(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnOutputDirectoryPathChanged(string value)
        {
            IsOutputDirectoryPathSet = !string.IsNullOrEmpty(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnVOActorSubstringChanged(string value)
        {
            IsVOActorSubstringSet = !string.IsNullOrEmpty(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnWemsDirectoryPathChanged(string value)
        {
            IsWemsDirectoryPathSet = !string.IsNullOrEmpty(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnBnksDirectoryPathChanged(string value)
        {
            IsBnksDirectoryPathSet = !string.IsNullOrEmpty(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnSoundbanksInfoXmlPathChanged(string value)
        {
            IsSoundbanksInfoXmlSet = !string.IsNullOrEmpty(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnIsUsingWwiseProjectChanged(bool value)
        {
            IsUsingWwiseProject = value;
            UpdateOkButtonIsEnabled();
        }

        private void UpdateOkButtonIsEnabled()
        {
            IsOkButtonEnabled = IsAudioProjectNameSet && IsOutputDirectoryPathSet && IsWemsDirectoryPathSet && IsBnksDirectoryPathSet && IsVOActorSubstringSet && ((IsUsingWwiseProject && IsSoundbanksInfoXmlSet) || !IsUsingWwiseProject);
        }

        [RelayCommand] public void SetOutputDirectoryPath()
        {
            var result = _standardDialogs.DisplayBrowseFolderDialog();
            if (result.Result)
            {
                var filePath = result.Folder;
                OutputDirectoryPath = filePath;
                _logger.Here().Information($"Audio Project directory set to: {filePath}");
            }
        }

        [RelayCommand] public void SetWemsDirectoryPath()
        {
            using var dialog = new FolderBrowserDialog();
            var result = dialog.ShowDialog();
            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                WemsDirectoryPath = dialog.SelectedPath;
        }

        [RelayCommand] public void SetBnksDirectoryPath()
        {
            using var dialog = new FolderBrowserDialog();
            var result = dialog.ShowDialog();
            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                BnksDirectoryPath = dialog.SelectedPath;
        }

        [RelayCommand] public void SetSoundbanksInfoXmlPath()
        {
            using var openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "Select SoundbanksInfo.xml file";
            openFileDialog.Filter = "SoundbanksInfo.xml|SoundbanksInfo.xml";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
                SoundbanksInfoXmlPath = openFileDialog.FileName;
        }

        [RelayCommand] public void CloseWindowAction()
        {
            _closeAction?.Invoke();
        }

        public void SetCloseAction(System.Action closeAction)
        {
            _closeAction = closeAction;
        }
    }
}
