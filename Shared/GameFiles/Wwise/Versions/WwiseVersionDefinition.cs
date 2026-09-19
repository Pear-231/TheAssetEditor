using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Shared.GameFormats.Wwise.Versions
{
    public abstract class WwiseVersionDefinition
    {
        private readonly HircFactory _hircFactory;

        private protected WwiseVersionDefinition(
            uint rawBankGeneratorVersion,
            uint schemaVersion,
            string displayName,
            HircFactory hircFactory)
        {
            RawBankGeneratorVersion = rawBankGeneratorVersion;
            SchemaVersion = schemaVersion;
            DisplayName = displayName;
            _hircFactory = hircFactory;
        }

        public uint RawBankGeneratorVersion { get; }
        public uint SchemaVersion { get; }
        public string DisplayName { get; }

        public abstract bool UsesVariableActionExceptionCount { get; }
        public abstract bool HasDangerousVirtualVoiceLimit { get; }
        public abstract bool HasAcousticTextures { get; }

        public HircItem CreateHirc(AkBkHircType type) => _hircFactory.CreateInstance(type);

        public abstract AkBkHircType DecodeHircType(byte rawType);
        public abstract byte EncodeHircType(AkBkHircType type);
        public abstract AkActionParameter ClassifyAction(AkActionType actionType);
        public abstract bool TryMapProperty(byte rawPropertyId, out WwiseProperty property);
    }
}
