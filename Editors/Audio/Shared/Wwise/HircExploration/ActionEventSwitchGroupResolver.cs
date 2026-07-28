using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Wwise.HircExploration
{
    public interface IActionEventSwitchGroupResolver
    {
        IReadOnlyList<HircSwitchGroup> GetSwitchGroups(string actionEventName);
    }

    public class ActionEventSwitchGroupResolver(IAudioRepository audioRepository, IHircGraphService hircGraphService) : IActionEventSwitchGroupResolver
    {
        private readonly IAudioRepository _audioRepository = audioRepository;
        private readonly IHircGraphService _hircGraphService = hircGraphService;

        public IReadOnlyList<HircSwitchGroup> GetSwitchGroups(string actionEventName)
        {
            if (string.IsNullOrWhiteSpace(actionEventName))
                return [];

            _audioRepository.Load([Wh3LanguageInformation.GetLanguageAsString(Wh3Language.EnglishUK)]);

            var actionEventId = WwiseHash.Compute(actionEventName);
            var actionEvent = _audioRepository.GetHircs(actionEventId).FirstOrDefault(x => x is ICAkEvent);
            if (actionEvent == null)
                return [];

            return _hircGraphService.FindSwitchGroups(actionEvent);
        }
    }
}
