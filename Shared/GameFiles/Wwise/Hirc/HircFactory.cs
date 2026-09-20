using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc
{
    public class HircFactory
    {
        private readonly Dictionary<AkBkHircType, Func<HircItem>> _itemList = [];

        public void RegisterHirc(AkBkHircType type, Func<HircItem> creator)
        {
            _itemList[type] = creator;
        }

        public HircItem CreateInstance(AkBkHircType type)
        {
            if (_itemList.TryGetValue(type, out var functor))
                return functor();

            return new UnknownHircItem();
        }

        public static HircFactory CreateFactory_v112()
        {
            var instance = new HircFactory();
            instance.RegisterHirc(AkBkHircType.ActorMixer, () => new V112.CAkActorMixer_V112());
            instance.RegisterHirc(AkBkHircType.Sound, () => new V112.CAkSound_V112());
            instance.RegisterHirc(AkBkHircType.Event, () => new V112.CAkEvent_V112());
            instance.RegisterHirc(AkBkHircType.Action, () => new V112.CAkAction_V112());
            instance.RegisterHirc(AkBkHircType.SwitchContainer, () => new V112.CAkSwitchCntr_V112());
            instance.RegisterHirc(AkBkHircType.RandomSequenceContainer, () => new V112.CAkRanSeqCntr_V112());
            instance.RegisterHirc(AkBkHircType.LayerContainer, () => new V112.CAkLayerCntr_V112());
            instance.RegisterHirc(AkBkHircType.Dialogue_Event, () => new V112.CAkDialogueEvent_V112());
            return instance;
        }

        public static HircFactory CreateFactory_v136()
        {
            var instance = new HircFactory();
            instance.RegisterHirc(AkBkHircType.ActorMixer, () => new V136.CAkActorMixer_V136());
            instance.RegisterHirc(AkBkHircType.Sound, () => new V136.CAkSound_V136());
            instance.RegisterHirc(AkBkHircType.Event, () => new V136.CAkEvent_V136());
            instance.RegisterHirc(AkBkHircType.Action, () => new V136.CAkAction_V136());
            instance.RegisterHirc(AkBkHircType.SwitchContainer, () => new V136.CAkSwitchCntr_V136());
            instance.RegisterHirc(AkBkHircType.RandomSequenceContainer, () => new V136.CAkRanSeqCntr_V136());
            instance.RegisterHirc(AkBkHircType.LayerContainer, () => new V136.CAkLayerCntr_V136());
            instance.RegisterHirc(AkBkHircType.Dialogue_Event, () => new V136.CAkDialogueEvent_V136());
            instance.RegisterHirc(AkBkHircType.Music_Track, () => new V136.CAkMusicTrack_V136());
            instance.RegisterHirc(AkBkHircType.Music_Segment, () => new V136.CAkMusicSegment_V136());
            instance.RegisterHirc(AkBkHircType.Music_Random_Sequence, () => new V136.CAkMusicRanSeqCntr_V136());
            instance.RegisterHirc(AkBkHircType.Music_Switch, () => new V136.CAkMusicSwitchCntr_V136());
            instance.RegisterHirc(AkBkHircType.FxCustom, () => new V136.CAkFxCustom_V136());
            instance.RegisterHirc(AkBkHircType.FxShareSet, () => new V136.CAkFxShareSet_V136());
            instance.RegisterHirc(AkBkHircType.Audio_Bus, () => new V136.CAkBus_V136());
            return instance;
        }

        public HircItem ReadHirc(string filePath, ByteChunk chunk, BankVersion bankVersion, uint languageId, bool isCA, uint itemIndex, int? expectedLength = null)
        {
            if (expectedLength.HasValue && expectedLength.Value < HircHeader.Size)
                throw new InvalidDataException($"HIRC item {itemIndex} is only {expectedLength.Value} bytes.");

            var itemStartIndex = chunk.Index;
            var hircType = bankVersion.DecodeHircType(chunk.PeakByte());
            HircItem hircItem;

            try
            {
                hircItem = CreateInstance(hircType);
                hircItem.IndexInFile = itemIndex;
                hircItem.BnkFilePath = filePath;
                hircItem.LanguageId = languageId;
                hircItem.IsCA = isCA;
                hircItem.HircType = hircType;
                hircItem.ReadHirc(chunk, bankVersion);
            }
            catch (Exception exception)
            {
                chunk.Index = itemStartIndex;
                hircItem = new UnknownHircItem
                {
                    ErrorMsg = exception.Message,
                    BnkFilePath = filePath,
                    HircType = hircType
                };
                hircItem.ReadHirc(chunk, bankVersion);
            }

            var bytesRead = chunk.Index - itemStartIndex;
            if (expectedLength.HasValue && bytesRead != expectedLength.Value)
                throw new InvalidDataException($"HIRC item {itemIndex} expected {expectedLength.Value} bytes but read {bytesRead}.");

            return hircItem;
        }
    }
}
