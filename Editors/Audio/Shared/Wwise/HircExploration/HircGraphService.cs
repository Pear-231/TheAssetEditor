using Editors.Audio.Shared.Storage;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Wwise.HircExploration
{
    public record HircSwitchValue(uint Id, string Name, IReadOnlyList<uint> ChildIds);

    public record HircSwitchGroup(uint Id, string Name, uint DefaultValueId, string DefaultValueName, IReadOnlyList<HircSwitchValue> Values);

    public interface IHircGraphService
    {
        HircSwitchGroup GetSwitchGroup(ICAkSwitchCntr switchContainer);
        IReadOnlyList<HircSwitchGroup> FindSwitchGroups(HircItem root);
    }

    public class HircGraphService(IAudioRepository audioRepository) : IHircGraphService
    {
        private readonly IAudioRepository _audioRepository = audioRepository;

        public HircSwitchGroup GetSwitchGroup(ICAkSwitchCntr switchContainer)
        {
            var values = switchContainer.SwitchList
                .Select(value => new HircSwitchValue(value.SwitchId, GetSwitchValueName(value.SwitchId), value.NodeIdList))
                .ToArray();

            return new HircSwitchGroup(
                switchContainer.GroupId,
                _audioRepository.GetNameFromId(switchContainer.GroupId),
                switchContainer.DefaultSwitch,
                GetSwitchValueName(switchContainer.DefaultSwitch),
                values);
        }

        public IReadOnlyList<HircSwitchGroup> FindSwitchGroups(HircItem root)
        {
            var switchGroups = new Dictionary<uint, HircSwitchGroup>();
            var visited = new HashSet<HircItem>();
            Visit(root, visited, switchGroups);
            return switchGroups.Values
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void Visit(HircItem hirc, HashSet<HircItem> visited, Dictionary<uint, HircSwitchGroup> switchGroups)
        {
            if (!visited.Add(hirc))
                return;

            if (hirc is ICAkSwitchCntr switchContainer)
            {
                switchGroups.TryAdd(switchContainer.GroupId, GetSwitchGroup(switchContainer));
                Visit(switchContainer.SwitchList.SelectMany(x => x.NodeIdList), visited, switchGroups);
                return;
            }

            if (hirc is ICAkEvent actionEvent)
            {
                Visit(actionEvent.GetActionIds(), visited, switchGroups);
                return;
            }

            if (hirc is ICAkAction action)
            {
                if (action.GetActionType() == AkActionType.SetState)
                {
                    var stateGroupId = action.GetStateGroupId();
                    var matchingSwitches = _audioRepository
                        .GetHircsByHircType(AkBkHircType.SwitchContainer)
                        .OfType<ICAkSwitchCntr>()
                        .Where(x => x.GroupId == stateGroupId)
                        .OfType<HircItem>();

                    foreach (var matchingSwitch in matchingSwitches)
                        Visit(matchingSwitch, visited, switchGroups);
                }
                else
                    Visit(action.GetChildId(), visited, switchGroups);
                return;
            }

            var childIds = hirc switch
            {
                ICAkActorMixer actorMixer => actorMixer.GetChildren(),
                ICAkLayerCntr layerContainer => layerContainer.GetChildren(),
                ICAkRanSeqCntr randomSequenceContainer => randomSequenceContainer.GetChildren(),
                _ => []
            };
            Visit(childIds, visited, switchGroups);
        }

        private void Visit(IEnumerable<uint> hircIds, HashSet<HircItem> visited, Dictionary<uint, HircSwitchGroup> switchGroups)
        {
            foreach (var hircId in hircIds)
                Visit(hircId, visited, switchGroups);
        }

        private void Visit(uint hircId, HashSet<HircItem> visited, Dictionary<uint, HircSwitchGroup> switchGroups)
        {
            if (hircId == 0)
                return;

            var hirc = _audioRepository.GetHircs(hircId).FirstOrDefault();
            if (hirc != null)
                Visit(hirc, visited, switchGroups);
        }

        private string GetSwitchValueName(uint switchValueId)
        {
            if (switchValueId == 0)
                return "Any";
            return _audioRepository.GetNameFromId(switchValueId);
        }
    }
}
