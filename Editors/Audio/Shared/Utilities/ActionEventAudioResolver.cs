using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Utilities
{
    public interface IActionEventAudioResolver
    {
        byte[] ResolveFirstSound(string actionEventName, IReadOnlyDictionary<string, string> switchValues);
    }

    public class ActionEventAudioResolver(IAudioRepository audioRepository) : IActionEventAudioResolver
    {
        private readonly ILogger _logger = Logging.Create<ActionEventAudioResolver>();
        private readonly IAudioRepository _audioRepository = audioRepository;

        public byte[] ResolveFirstSound(string actionEventName, IReadOnlyDictionary<string, string> switchValues)
        {
            if (string.IsNullOrWhiteSpace(actionEventName))
                return null;

            _audioRepository.Load([Wh3LanguageInformation.GetLanguageAsString(Wh3Language.EnglishUK)]);

            var actionEventId = WwiseHash.Compute(actionEventName);
            var actionEvent = _audioRepository.GetHircs(actionEventId).FirstOrDefault(x => x is ICAkEvent);
            if (actionEvent == null)
            {
                _logger.Here().Warning($"Action event '{actionEventName}' ({actionEventId}) was not found");
                return null;
            }

            var sound = FindFirstSound(actionEvent, switchValues, []);
            if (sound == null)
            {
                _logger.Here().Warning($"No sound object was found beneath action event '{actionEventName}'");
                return null;
            }

            var sourceId = sound.GetSourceId();
            if (_audioRepository.DidxAudioListById.TryGetValue(sourceId, out var embeddedAudio))
            {
                var hirc = (HircItem)sound;
                var matchingBankAudio = embeddedAudio.FirstOrDefault(x =>
                    string.Equals(x.OwnerFilePath, hirc.BnkFilePath, StringComparison.OrdinalIgnoreCase));
                var selectedAudio = matchingBankAudio ?? embeddedAudio.First();
                _logger.Here().Information($"Playing {sourceId}.wem embedded in '{selectedAudio.OwnerFilePath}' for action event '{actionEventName}'");
                return selectedAudio.ByteArray;
            }

            var wemPackFile = _audioRepository.FindWem(sourceId.ToString());
            if (wemPackFile == null)
            {
                _logger.Here().Warning($"Sound object resolved to {sourceId}.wem, but that WEM was not found");
                return null;
            }

            _logger.Here().Information($"Playing {sourceId}.wem from '{wemPackFile.Name}' for action event '{actionEventName}'");
            return wemPackFile.DataSource.ReadData();
        }

        private ICAkSound FindFirstSound(HircItem hirc, IReadOnlyDictionary<string, string> switchValues, HashSet<HircItem> visited)
        {
            if (!visited.Add(hirc))
                return null;

            if (hirc is ICAkSound sound)
                return sound;

            var childIds = new List<uint>();
            if (hirc is ICAkEvent actionEvent)
                childIds = actionEvent.GetActionIds();
            else if (hirc is ICAkAction action && action.GetActionType() != AkActionType.SetState)
                childIds.Add(action.GetChildId());
            else if (hirc is ICAkSwitchCntr switchContainer)
                childIds = GetSelectedSwitchChildren(switchContainer, switchValues);
            else if (hirc is ICAkActorMixer actorMixer)
                childIds = actorMixer.GetChildren();
            else if (hirc is ICAkLayerCntr layerContainer)
                childIds = layerContainer.GetChildren();
            else if (hirc is ICAkRanSeqCntr randomSequenceContainer)
                childIds.Add(randomSequenceContainer.GetChildren().FirstOrDefault());

            foreach (var childId in childIds)
            {
                if (childId == 0)
                    continue;

                var child = _audioRepository.GetHircs(childId)
                    .FirstOrDefault(x => string.Equals(x.BnkFilePath, hirc.BnkFilePath, StringComparison.OrdinalIgnoreCase))
                    ?? _audioRepository.GetHircs(childId).FirstOrDefault();
                if (child == null)
                    continue;

                var result = FindFirstSound(child, switchValues, visited);
                if (result != null)
                    return result;
            }

            return null;
        }

        private List<uint> GetSelectedSwitchChildren(ICAkSwitchCntr switchContainer, IReadOnlyDictionary<string, string> switchValues)
        {
            var groupName = _audioRepository.GetNameFromId(switchContainer.GroupId);
            var selectedValueId = switchContainer.DefaultSwitch;
            if (switchValues.TryGetValue(groupName, out var selectedValue))
            {
                var matchingValue = switchContainer.SwitchList.FirstOrDefault(x =>
                    string.Equals(_audioRepository.GetNameFromId(x.SwitchId), selectedValue, StringComparison.OrdinalIgnoreCase));
                if (matchingValue != null)
                    selectedValueId = matchingValue.SwitchId;
            }

            return switchContainer.SwitchList
                .FirstOrDefault(x => x.SwitchId == selectedValueId)?
                .NodeIdList ?? [];
        }
    }
}
