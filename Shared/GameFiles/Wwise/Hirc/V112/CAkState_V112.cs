using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    // CAkState::SetInitialValues in Wwise 112 uses the older byte-count property bundle.
    public sealed class CAkState_V112 : HircItem, ICAkState
    {
        private readonly List<StateProperty> _properties = [];

        public IReadOnlyList<ICAkState.IAkStateProperty> GetProperties() => _properties;

        protected override void ReadData(ByteChunk chunk)
        {
            var propertyCount = chunk.ReadByte();
            for (var index = 0; index < propertyCount; index++)
                _properties.Add(new StateProperty { PropertyId = chunk.ReadByte() });
            for (var index = 0; index < propertyCount; index++)
                _properties[index].Value = chunk.ReadSingle();
        }

        public override byte[] WriteData() => throw new NotSupportedException("Writing state HIRCs is not supported.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Writing state HIRCs is not supported.");

        private sealed class StateProperty : ICAkState.IAkStateProperty
        {
            public ushort PropertyId { get; set; }
            public float Value { get; set; }
        }
    }
}
