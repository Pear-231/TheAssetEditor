using Shared.GameFormats.Wwise.Enums;

namespace Shared.GameFormats.Wwise.Hirc
{
    internal sealed class HircFactory
    {
        private readonly Dictionary<AkBkHircType, Func<HircItem>> _itemList = [];

        internal void RegisterHirc(AkBkHircType type, Func<HircItem> creator)
        {
            _itemList[type] = creator;
        }

        internal HircItem CreateInstance(AkBkHircType type)
        {
            if (_itemList.TryGetValue(type, out var functor))
                return functor();

            return new UnknownHircItem();
        }

    }
}
