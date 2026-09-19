using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    public class CAkBus_V136 : HircItem, ICAkBus
    {
        public uint OverrideBusId { get; set; }
        public uint IdDeviceShareset { get; set; }
        public BusInitialParams_V136 BusInitialParams { get; set; } = new BusInitialParams_V136();
        // Milliseconds, signed, not a float. Settled 2026-08-31 against init.bnk: every bus in
        // Warhammer III authors 1000, which is 0x000003E8 -- a denormal if read as a float. The
        // width is the same either way, so no byte count can catch this; only the value can.
        public int RecoveryTime { get; set; }
        public float MaxDuckVolume { get; set; }
        public DuckList_V136 DuckList { get; set; } = new DuckList_V136();
        public BusInitialFxParams_V136 BusInitialFxParams { get; set; } = new BusInitialFxParams_V136();
        public byte OverrideAttachmentParams { get; set; }
        public InitialRtpc_V136 InitialRtpc { get; set; } = new InitialRtpc_V136();
        public StateChunk_V136 StateChunk { get; set; } = new StateChunk_V136();

        protected override void ReadData(ByteChunk chunk)
        {
            OverrideBusId = chunk.ReadUInt32();
            if (OverrideBusId == 0)
                IdDeviceShareset = chunk.ReadUInt32();
            BusInitialParams.ReadData(chunk);
            RecoveryTime = chunk.ReadInt32();
            MaxDuckVolume = chunk.ReadSingle();
            DuckList.ReadData(chunk);
            BusInitialFxParams.ReadData(chunk);
            OverrideAttachmentParams = chunk.ReadByte();
            InitialRtpc.ReadData(chunk);
            StateChunk.ReadData(chunk);
        }

        // We don't need to make CAkBus objects because we can route audio through the existing busses as hircs appear to be shared between Banks.
        public override byte[] WriteData() => throw new NotSupportedException("Users probably don't need this complexity.");
        public override void UpdateSectionSize() => throw new NotSupportedException("Users probably don't need this complexity.");

        public uint GetOutputBusId() => OverrideBusId;

        public AuthoredProperties GetProperties()
            => WwisePropertyMap_V136.Read(BusInitialParams.AkPropBundle, new AkPropBundleMinMax_V136());

        public IReadOnlyList<uint> GetEffectIds()
        {
            var effectIds = new List<uint>(BusInitialFxParams.FxChunk.Count + 1);
            foreach (var effect in BusInitialFxParams.FxChunk)
            {
                if (effect.FxId != 0)
                    effectIds.Add(effect.FxId);
            }
            if (BusInitialFxParams.FxId0 != 0)
                effectIds.Add(BusInitialFxParams.FxId0);
            return effectIds;
        }

        public IReadOnlyList<uint> GetAuxiliaryBusIds()
        {
            var auxiliaryBusIds = new List<uint>(5);
            AddIfPresent(auxiliaryBusIds, BusInitialParams.AuxParams.AuxBus0);
            AddIfPresent(auxiliaryBusIds, BusInitialParams.AuxParams.AuxBus1);
            AddIfPresent(auxiliaryBusIds, BusInitialParams.AuxParams.AuxBus2);
            AddIfPresent(auxiliaryBusIds, BusInitialParams.AuxParams.AuxBus3);
            AddIfPresent(auxiliaryBusIds, BusInitialParams.AuxParams.ReflectionsAuxBus);
            return auxiliaryBusIds;
        }

        private static void AddIfPresent(List<uint> busIds, uint busId)
        {
            if (busId != 0)
                busIds.Add(busId);
        }

        public class BusInitialParams_V136
        {
            public AkPropBundle_V136 AkPropBundle { get; set; } = new AkPropBundle_V136();
            public PositioningParams_V136 PositioningParams { get; set; } = new PositioningParams_V136();
            public AuxParams_V136 AuxParams { get; set; } = new AuxParams_V136();
            public byte BitVector1 { get; set; }
            public ushort MaxNumInstance { get; set; }
            public uint ChannelConfig { get; set; }
            public byte BitVector2 { get; set; }
            public void ReadData(ByteChunk chunk)
            {
                AkPropBundle.ReadData(chunk);
                PositioningParams.ReadData(chunk);
                AuxParams.ReadData(chunk);
                BitVector1 = chunk.ReadByte();
                MaxNumInstance = chunk.ReadUShort();
                ChannelConfig = chunk.ReadUInt32();
                BitVector2 = chunk.ReadByte();
            }
        }

        public class DuckList_V136
        {
            public uint UlDucks { get; set; }
            public List<AkDuckInfo_V136> Ducks { get; set; } = [];

            public class AkDuckInfo_V136
            {
                public uint BusId { get; set; }
                public float DuckVolume { get; set; }
                // Milliseconds, signed, for the same reason RecoveryTime is: init.bnk authors
                // 100, 200, 300, 400, 500, 800 and 1000 here.
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
                for (uint i = 0; i < UlDucks; i++)
                {
                    var akDuckInfo = new AkDuckInfo_V136();
                    akDuckInfo.ReadData(chunk);
                    Ducks.Add(akDuckInfo);
                }
            }
        }

        public class BusInitialFxParams_V136
        {
            public byte NumFx { get; set; }
            public byte BitsFxBypass { get; set; }
            public List<FxChunk_V136> FxChunk { get; set; } = [];
            public uint FxId0 { get; set; }
            public byte IsShareSet0 { get; set; }

            public void ReadData(ByteChunk chunk)
            {
                NumFx = chunk.ReadByte();
                if (NumFx != 0)
                    BitsFxBypass = chunk.ReadByte();

                for (uint i = 0; i < NumFx; i++)
                {
                    var fxChunk = new FxChunk_V136();
                    fxChunk.ReadData(chunk);
                    FxChunk.Add(fxChunk);
                }

                FxId0 = chunk.ReadUInt32();
                IsShareSet0 = chunk.ReadByte();
            }
        }
    }
}
