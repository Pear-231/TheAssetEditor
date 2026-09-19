using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    // The master-mixer node as bank generator 112 writes it.
    //
    // Built from wwiser's CAkBus__SetInitialValues version gates rather than from the V136 class,
    // because three of them fall between the two versions: 112 has no device shareset (that arrives
    // at 127), carries no positioning or auxiliary parameters inside its initial params (they
    // arrive at 123), and still writes its effects inline rather than through the effect slots
    // that replace them at 136.
    public class CAkBus_V112 : HircItem, ICAkBus
    {
        public uint OverrideBusId { get; set; }
        public BusInitialParams_V112 BusInitialParams { get; set; } = new BusInitialParams_V112();

        // Milliseconds, signed, exactly as in V136 -- Attila's init.bnk authors 1000 here, which is
        // a denormal read as a float.
        public int RecoveryTime { get; set; }
        public float MaxDuckVolume { get; set; }
        public DuckList_V112 DuckList { get; set; } = new DuckList_V112();
        public BusInitialFxParams_V112 BusInitialFxParams { get; set; } = new BusInitialFxParams_V112();
        public byte OverrideAttachmentParams { get; set; }
        public InitialRtpc_V112 InitialRtpc { get; set; } = new InitialRtpc_V112();
        public StateChunk_V112 StateChunk { get; set; } = new StateChunk_V112();

        protected override void ReadData(ByteChunk chunk)
        {
            OverrideBusId = chunk.ReadUInt32();
            BusInitialParams.ReadData(chunk);
            RecoveryTime = chunk.ReadInt32();
            MaxDuckVolume = chunk.ReadSingle();
            DuckList.ReadData(chunk);
            BusInitialFxParams.ReadData(chunk);
            OverrideAttachmentParams = chunk.ReadByte();
            InitialRtpc.ReadData(chunk);
            StateChunk.ReadData(chunk);
        }

        // Buses are never authored by this editor: routing goes through the ones the game already
        // has, and this repo only writes V136 banks in any case.
        public override byte[] WriteData() => throw new NotSupportedException("Writing V112 buses is not supported.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Writing V112 buses is not supported.");

        public uint GetOutputBusId() => OverrideBusId;

        public AuthoredProperties GetProperties()
            => WwisePropertyMap_V112.Read(BusInitialParams.AkPropBundle, new AkPropBundleMinMax_V112());

        public IReadOnlyList<uint> GetEffectIds()
        {
            var effectIds = new List<uint>(BusInitialFxParams.FxList.Count + 1);
            foreach (var effect in BusInitialFxParams.FxList)
            {
                if (effect.FxId != 0)
                    effectIds.Add(effect.FxId);
            }
            if (BusInitialFxParams.FxId0 != 0)
                effectIds.Add(BusInitialFxParams.FxId0);
            return effectIds;
        }

        // V112 keeps no auxiliary sends on the bus itself -- AuxParams only reach a bus from 123
        // onwards -- so there is nothing here to report rather than nothing found.
        public IReadOnlyList<uint> GetAuxiliaryBusIds() => [];

        public class BusInitialParams_V112
        {
            public AkPropBundle_V112 AkPropBundle { get; set; } = new AkPropBundle_V112();
            public byte BitVector1 { get; set; }
            public byte BitVector2 { get; set; }
            public ushort MaxNumInstance { get; set; }
            public uint ChannelConfig { get; set; }
            public byte BitVector3 { get; set; }

            public void ReadData(ByteChunk chunk)
            {
                AkPropBundle.ReadData(chunk);
                BitVector1 = chunk.ReadByte();
                BitVector2 = chunk.ReadByte();
                MaxNumInstance = chunk.ReadUShort();
                ChannelConfig = chunk.ReadUInt32();
                BitVector3 = chunk.ReadByte();
            }
        }

        public class DuckList_V112
        {
            public uint UlDucks { get; set; }
            public List<AkDuckInfo_V112> Ducks { get; set; } = [];

            public class AkDuckInfo_V112
            {
                public uint BusId { get; set; }
                public float DuckVolume { get; set; }
                public int FadeOutTime { get; set; }
                public int FadeInTime { get; set; }
                public byte FadeCurve { get; set; }
                public byte TargetProp { get; set; }

                public void ReadData(ByteChunk chunk)
                {
                    BusId = chunk.ReadUInt32();
                    DuckVolume = chunk.ReadSingle();
                    FadeOutTime = chunk.ReadInt32();
                    FadeInTime = chunk.ReadInt32();
                    FadeCurve = chunk.ReadByte();
                    TargetProp = chunk.ReadByte();
                }
            }

            public void ReadData(ByteChunk chunk)
            {
                UlDucks = chunk.ReadUInt32();
                for (uint duckIndex = 0; duckIndex < UlDucks; duckIndex++)
                {
                    var akDuckInfo = new AkDuckInfo_V112();
                    akDuckInfo.ReadData(chunk);
                    Ducks.Add(akDuckInfo);
                }
            }
        }

        // Effects written inline. From 136 a bus carries effect slots instead, which is why this
        // shape belongs to the version rather than being shared with CAkBus_V136.
        public class BusInitialFxParams_V112
        {
            public byte NumFx { get; set; }
            public byte BitsFxBypass { get; set; }
            public List<FxChunk_V112> FxList { get; set; } = [];
            public uint FxId0 { get; set; }
            public byte IsShareSet0 { get; set; }

            public void ReadData(ByteChunk chunk)
            {
                NumFx = chunk.ReadByte();
                if (NumFx != 0)
                {
                    BitsFxBypass = chunk.ReadByte();
                    for (var fxIndex = 0; fxIndex < NumFx; fxIndex++)
                    {
                        var fxChunk = new FxChunk_V112();
                        fxChunk.ReadData(chunk);
                        FxList.Add(fxChunk);
                    }
                }

                FxId0 = chunk.ReadUInt32();
                IsShareSet0 = chunk.ReadByte();
            }
        }
    }
}
