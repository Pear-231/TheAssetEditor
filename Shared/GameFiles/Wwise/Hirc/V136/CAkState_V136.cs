using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    // CAkState::SetInitialValues in Wwise 136: u16 count, all u16 RTPC parameter ids, then all f32 values.
    public sealed class CAkState_V136 : HircItem, ICAkState
    {
        private readonly List<StateProperty> _properties = [];

        public IReadOnlyList<ICAkState.IAkStateProperty> GetProperties() => _properties;

        protected override void ReadData(ByteChunk chunk)
        {
            var propertyCount = chunk.ReadUShort();
            for (var index = 0; index < propertyCount; index++)
                _properties.Add(new StateProperty { PropertyId = chunk.ReadUShort() });
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
