using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    // CAkModulator's binary body is shared by the LFO, envelope and time modulators in V136.
    // The ids are deliberately raw: they use AkModulatorPropID, not AkPropId_V136.
    public abstract class CAkModulator_V136 : HircItem
    {
        public ModulatorPropertyBundle_V136 Properties { get; } = new();
        public ModulatorRangeBundle_V136 Ranges { get; } = new();
        public InitialRtpc_V136 InitialRtpc { get; } = new();

        protected override void ReadData(ByteChunk chunk)
        {
            Properties.ReadData(chunk);
            Ranges.ReadData(chunk);
            InitialRtpc.ReadData(chunk);
        }

        public override byte[] WriteData() => throw new NotSupportedException("Writing modulator HIRCs is not supported.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Writing modulator HIRCs is not supported.");
    }

    public sealed class CAkLfoModulator_V136 : CAkModulator_V136 { }
    public sealed class CAkEnvelopeModulator_V136 : CAkModulator_V136 { }
    public sealed class CAkTimeModulator_V136 : CAkModulator_V136 { }

    public sealed class ModulatorPropertyBundle_V136
    {
        public List<ModulatorProperty_V136> Properties { get; } = [];

        public void ReadData(ByteChunk chunk)
        {
            var count = chunk.ReadByte();
            for (var index = 0; index < count; index++)
                Properties.Add(new ModulatorProperty_V136 { PropertyId = chunk.ReadByte() });
            for (var index = 0; index < count; index++)
                Properties[index].Value = chunk.ReadUInt32();
        }
    }

    public sealed class ModulatorRangeBundle_V136
    {
        public List<ModulatorRange_V136> Ranges { get; } = [];

        public void ReadData(ByteChunk chunk)
        {
            var count = chunk.ReadByte();
            for (var index = 0; index < count; index++)
                Ranges.Add(new ModulatorRange_V136 { PropertyId = chunk.ReadByte() });
            for (var index = 0; index < count; index++)
            {
                Ranges[index].Minimum = chunk.ReadUInt32();
                Ranges[index].Maximum = chunk.ReadUInt32();
            }
        }
    }

    public sealed class ModulatorProperty_V136
    {
        public byte PropertyId { get; set; }
        public uint Value { get; set; }
    }

    public sealed class ModulatorRange_V136
    {
        public byte PropertyId { get; set; }
        public uint Minimum { get; set; }
        public uint Maximum { get; set; }
    }
}
