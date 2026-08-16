using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise.HircExploration;
using Shared.Core.PackFiles;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Utilities
{
    public sealed class ActionEventResolver(IAudioRepository audioRepository, IHircGraphService hircGraphService, IPackFileService packFileService)
    {
        private readonly ILogger _logger = Logging.Create<ActionEventResolver>();
        private readonly IAudioRepository _audioRepository = audioRepository;
        private readonly IHircGraphService _hircGraphService = hircGraphService;
        private readonly IPackFileService _packFileService = packFileService;

        public IReadOnlyList<HircSwitchGroup> GetSwitchGroups(string actionEventName)
        {
            if (string.IsNullOrWhiteSpace(actionEventName))
                return [];

            var actionEvent = _audioRepository.FindActionEvent(actionEventName);
            if (actionEvent == null)
                _logger.Here().Warning($"Action event '{actionEventName}' was not found");
            return actionEvent == null ? [] : _hircGraphService.FindSwitchGroups(actionEvent);
        }

        public ResolvedWem ResolveFirstSound(string actionEventName, IReadOnlyDictionary<string, string> switchValuesByGroupName)
        {
            if (string.IsNullOrWhiteSpace(actionEventName))
                return null;

            var actionEventHirc = _audioRepository.FindActionEvent(actionEventName);
            if (actionEventHirc == null)
            {
                _logger.Here().Warning($"Action event '{actionEventName}' was not found");
                return null;
            }

            var resolvedSoundHirc = FindFirstSound(actionEventHirc, switchValuesByGroupName, []);
            if (resolvedSoundHirc == null)
            {
                _logger.Here().Warning($"No sound object was found beneath action event '{actionEventName}'");
                return null;
            }

            var wemId = resolvedSoundHirc.GetSourceId();
            var embeddedWemMatches = _audioRepository.FindDidxWem(wemId);
            if (embeddedWemMatches.Count != 0)
            {
                var resolvedSoundHircItem = (HircItem)resolvedSoundHirc;
                var matchingBankWem = embeddedWemMatches.FirstOrDefault(embeddedWem =>
                    string.Equals(embeddedWem.OwnerFilePath, resolvedSoundHircItem.BnkFilePath, StringComparison.OrdinalIgnoreCase));
                var selectedEmbeddedWem = matchingBankWem ?? embeddedWemMatches.First();
                _logger.Here().Information($"Action event '{actionEventName}' resolved to {wemId}.wem embedded in '{selectedEmbeddedWem.OwnerFilePath}'");
                return new ResolvedWem(wemId, selectedEmbeddedWem.OwnerFilePath, IsDidx: true, selectedEmbeddedWem.ByteArray);
            }

            var wemPackFile = _audioRepository.FindWem(wemId.ToString());
            if (wemPackFile == null)
            {
                _logger.Here().Warning($"Action event '{actionEventName}' resolved to {wemId}.wem, but that WEM was not found");
                return null;
            }

            var wemFilePath = _packFileService.GetFullPath(wemPackFile);
            _logger.Here().Information($"Action event '{actionEventName}' resolved to {wemId}.wem in '{wemFilePath}'");
            return new ResolvedWem(wemId, wemFilePath, IsDidx: false, wemPackFile.DataSource.ReadData());
        }

        private ICAkSound FindFirstSound(HircItem currentHircItem, IReadOnlyDictionary<string, string> switchValuesByGroupName, HashSet<HircItem> visitedHircItems)
        {
            if (!visitedHircItems.Add(currentHircItem))
                return null;

            if (currentHircItem is ICAkSound soundHirc)
                return soundHirc;

            var childHircIds = new List<uint>();
            if (currentHircItem is ICAkEvent actionEventHirc)
                childHircIds = actionEventHirc.GetActionIds();
            else if (currentHircItem is ICAkAction actionHirc && actionHirc.GetActionType() != AkActionType.SetState)
                childHircIds.Add(actionHirc.GetChildId());
            else if (currentHircItem is ICAkSwitchCntr switchContainerHirc)
                childHircIds = GetSelectedSwitchChildren(switchContainerHirc, switchValuesByGroupName);
            else if (currentHircItem is ICAkActorMixer actorMixerHirc)
                childHircIds = actorMixerHirc.GetChildren();
            else if (currentHircItem is ICAkLayerCntr layerContainerHirc)
                childHircIds = layerContainerHirc.GetChildren();
            else if (currentHircItem is ICAkRanSeqCntr randomSequenceContainerHirc)
                childHircIds.Add(randomSequenceContainerHirc.GetChildren().FirstOrDefault());

            foreach (var childHircId in childHircIds)
            {
                if (childHircId == 0)
                    continue;

                var childHircItems = _audioRepository.GetHircs(childHircId);
                var childHircItem = childHircItems
                    .FirstOrDefault(hircItem => string.Equals(hircItem.BnkFilePath, currentHircItem.BnkFilePath, StringComparison.OrdinalIgnoreCase))
                    ?? childHircItems.FirstOrDefault();
                if (childHircItem == null)
                    continue;

                var resolvedSoundHirc = FindFirstSound(childHircItem, switchValuesByGroupName, visitedHircItems);
                if (resolvedSoundHirc != null)
                    return resolvedSoundHirc;
            }

            return null;
        }

        private List<uint> GetSelectedSwitchChildren(ICAkSwitchCntr switchContainer, IReadOnlyDictionary<string, string> switchValuesByGroupName)
        {
            var groupName = _audioRepository.GetNameFromId(switchContainer.GroupId);
            var selectedSwitchValueId = switchContainer.DefaultSwitch;
            if (switchValuesByGroupName.TryGetValue(groupName, out var selectedSwitchValue))
            {
                var matchingSwitchValue = switchContainer.SwitchList.FirstOrDefault(switchValue =>
                    string.Equals(_audioRepository.GetNameFromId(switchValue.SwitchId), selectedSwitchValue, StringComparison.OrdinalIgnoreCase));
                if (matchingSwitchValue != null)
                    selectedSwitchValueId = matchingSwitchValue.SwitchId;
            }

            return switchContainer.SwitchList
                .FirstOrDefault(switchValue => switchValue.SwitchId == selectedSwitchValueId)?
                .NodeIdList ?? [];
        }
    }
}
