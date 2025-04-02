using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Editors.Audio.AudioEditor;
using Editors.Audio.AudioEditor.AudioProjectData;
using Editors.Audio.AudioEditor.AudioSettings;
using Editors.Audio.Storage;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Utility
{
    public class SoundBankToAudioProjectConverter
    {
        private readonly IAudioRepository _audioRepository;
        private readonly IAudioEditorService _audioEditorService;

        public SoundBankToAudioProjectConverter(IAudioRepository audioRepository, IAudioEditorService audioEditorService)
        {
            _audioRepository = audioRepository;
            _audioEditorService = audioEditorService;

            return;

            // In the audio editor have a button for the user to select some soundbanks and make an audio project out of them.

            var wavNameLookupByWemID = new Dictionary<uint, string>();
            BuildWavNameByWemIDLookup(wavNameLookupByWemID);

            var dialogueEvents = _audioRepository.GetHircItemsByType<ICAkDialogueEvent>();
            foreach (var dialogueEvent in dialogueEvents)
            {
                var hircItem = dialogueEvent as HircItem;
                var dialogueEventName = _audioRepository.GetNameFromID(hircItem.ID);
                if (dialogueEventName != "battle_vo_order_attack")
                    continue;

                Console.WriteLine($"Dialogue Event - {dialogueEventName}");
                ProcessDialogueEventV136(dialogueEvent, wavNameLookupByWemID);
            }
        }

        private static void BuildWavNameByWemIDLookup(Dictionary<uint, string> wavNameLookupByWemID)
        {
            var soundbanksInfoXmlPath = @"C:\Users\George Bates\Downloads\Audio Mixer\GeneratedSoundBanks\Windows\SoundbanksInfo.xml";
            var streamedFilesElement = XDocument.Load(soundbanksInfoXmlPath).Root.Element("StreamedFiles");
            foreach (var fileElement in streamedFilesElement.Elements("File"))
            {
                var id = fileElement.Attribute("Id")?.Value;
                var shortName = fileElement.Element("ShortName")?.Value;
                wavNameLookupByWemID.Add(uint.Parse(id), shortName);
            }
        }

        private void ProcessDialogueEventV136(ICAkDialogueEvent dialogueEvent, Dictionary<uint, string> wavNameLookupByWemID)
        {
            var audioProject = AudioProject.CreateAudioProject();
            var audioFiles = new List<AudioFile>();

            var dialogueEventHirc = dialogueEvent as HircItem;
            var dialogueEventName = _audioRepository.GetNameFromID(dialogueEventHirc.ID);

            var audioProjectDialogueEvent = audioProject.SoundBanks
                .SelectMany(soundBank => soundBank.DialogueEvents)
                .FirstOrDefault(dialogueEvent => dialogueEvent.Name == dialogueEventName);

            var decisionPathHelper = new DecisionPathHelper(_audioRepository);
            var decisionPathCollection = decisionPathHelper.GetDecisionPaths(dialogueEvent);
            foreach (var statePath in decisionPathCollection.Paths)
            {
                var statePathNodes = new List<StatePathNode>();
                var stateGroupIndex = 0;

                foreach (var stateGroup in statePath.Items)
                {
                    var stateName = _audioRepository.GetNameFromID(stateGroup.Value);
                    if (stateName == "0")
                        stateName = "Any";

                    var stateGroupName = _audioRepository.GetNameFromID(dialogueEvent.Arguments[stateGroupIndex].GroupID);

                    statePathNodes.Add(new StatePathNode
                    {
                        StateGroup = new StateGroup { Name = stateGroupName },
                        State = new State { Name = stateName }
                    });

                    stateGroupIndex++;
                }
                
                if (!_audioRepository.HircLookupByID.TryGetValue(statePath.ChildNodeID, out var dialogueEventChildHirc) || statePath.ChildNodeID == 0)
                    continue;

                if (dialogueEventChildHirc.FirstOrDefault() is ICAkRanSeqCntr ranSeqCntrV136)
                {
                    foreach (var child in ranSeqCntrV136.GetChildren())
                    {
                        var ranSeqCntrChildHirc = _audioRepository.HircLookupByID[child].FirstOrDefault();
                        if (ranSeqCntrChildHirc is ICAkSound soundHirc)
                        {
                            var originalWavName = wavNameLookupByWemID[soundHirc.GetSourceID()];
                            
                            var newWavName = "";

                            // Skarr_Battle_vo_order_move-01
                            // campaign_vo_move_garrisoning
                            // campaign_vo_move
                            // campaign_vo_move_next_turn
                            // campaign_vo_stance_default
                            // campaign_vo_stance_double_time
                            // campaign_vo_stance_march
                            // battle_vo_order_move
                            // battle_vo_order_move_alternative

                            // VO_Khorne_Skarr_Bat_Move




                            var audioFile = new AudioFile()
                            {
                                FileName = originalWavName,
                                FilePath = $"D:\\{originalWavName}"
                            };

                            audioFiles.Add(audioFile);
                        }
                    }
                }


                var audioProjectStatePath = AudioProjectHelpers.CreateStatePathFromDialogueEvent(_audioRepository, statePathNodes, audioFiles);
                AudioProjectHelpers.InsertStatePathAlphabetically(audioProjectDialogueEvent, audioProjectStatePath);
                Console.WriteLine($"Processed State Path: {dialogueEventName}");
            }
        }
    }
}
