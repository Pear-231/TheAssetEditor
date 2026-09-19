using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    public class CAkAction_V112 : HircItem, ICAkAction
    {
        private static readonly WwiseVersionDefinition s_versionDefinition = WwiseVersionResolver.Resolve(BankVersion.Attila);

        public AkActionType ActionType { get; set; }
        public uint IdExt { get; set; }
        public byte IdExt4 { get; set; }
        public AkPropBundle_V112 AkPropBundle0 { get; set; } = new AkPropBundle_V112();
        public AkPropBundleMinMax_V112 AkPropBundle1 { get; set; } = new AkPropBundleMinMax_V112();
        public PlayActionParams_V112? PlayActionParams { get; set; }
        public StateActionParams_V112? StateActionParams { get; set; }
        public SwitchActionParams_V112? SwitchActionParams { get; set; }
        public byte[] AdditionalParameters { get; set; } = [];

        protected override void ReadData(ByteChunk chunk)
        {
            ActionType = (AkActionType)chunk.ReadUShort();
            IdExt = chunk.ReadUInt32();
            IdExt4 = chunk.ReadByte();
            AkPropBundle0. ReadData(chunk);
            AkPropBundle1.ReadData(chunk);

            if (IsPlay(ActionType))
                PlayActionParams = PlayActionParams_V112.ReadData(chunk);
            else if (ActionType == AkActionType.SetState)
                StateActionParams = StateActionParams_V112.ReadData(chunk);
            else if (ActionType == AkActionType.SetSwitch)
                SwitchActionParams = SwitchActionParams_V112.ReadData(chunk);
            else
                AdditionalParameters = ActionParameterReader.Read(chunk, ActionType, s_versionDefinition);
        }

        public override byte[] WriteData()
        {
            using var memStream = WriteHeader();
            memStream.Write(ByteParsers.UShort.EncodeValue((ushort)ActionType, out _));
            memStream.Write(ByteParsers.UInt32.EncodeValue(IdExt, out _));
            memStream.Write(ByteParsers.Byte.EncodeValue(IdExt4, out _));
            memStream.Write(AkPropBundle0.ReadData());
            memStream.Write(AkPropBundle1.ReadData());

            if (IsPlay(ActionType))
                memStream.Write(PlayActionParams!.WriteData());
            else if (ActionType == AkActionType.SetState)
                memStream.Write(StateActionParams!.WriteData());
            else if (ActionType == AkActionType.SetSwitch)
                memStream.Write(SwitchActionParams!.WriteData());
            else
                memStream.Write(AdditionalParameters);

            var byteArray = memStream.ToArray();

            // Reload the object to ensure sanity
            var sanityReload = new CAkAction_V112();
            sanityReload.ReadHirc(new ByteChunk(byteArray));

            return byteArray;
        }

        public override void UpdateSectionSize()
        {
            var idSize = ByteHelper.GetPropertyTypeSize(Id);
            var actionTypeSize = ByteHelper.GetPropertyTypeSize(ActionType);
            var idExtSize = ByteHelper.GetPropertyTypeSize(IdExt);
            var idExt4Size = ByteHelper.GetPropertyTypeSize(IdExt4);
            var akPropBundle0Size = AkPropBundle0.GetSize();
            var akPropBundle1Size = AkPropBundle1.GetSize();

            var parameterSize = IsPlay(ActionType) ? PlayActionParams!.GetSize()
                : ActionType == AkActionType.SetState ? StateActionParams!.GetSize()
                : ActionType == AkActionType.SetSwitch ? SwitchActionParams!.GetSize()
                : (uint)AdditionalParameters.Length;
            SectionSize = idSize + actionTypeSize + idExtSize + idExt4Size + akPropBundle0Size + akPropBundle1Size + parameterSize;
        }

        public AkActionType GetActionType() => ActionType;
        public AuthoredProperties GetProperties() => WwisePropertyMap_V112.Read(AkPropBundle0, AkPropBundle1);
        public uint GetChildId() => IdExt;
        public uint GetTargetStateId() => StateActionParams?.TargetStateId ?? 0;
        public uint GetSwitchGroupId() => SwitchActionParams?.SwitchGroupId ?? 0;
        public uint GetSwitchValueId() => SwitchActionParams?.SwitchValueId ?? 0;
        public uint GetStateGroupId() => StateActionParams?.StateGroupId ?? 0;

        private static bool IsPlay(AkActionType actionType)
            => s_versionDefinition.ClassifyAction(actionType) == AkActionParameter.Play;

        public class PlayActionParams_V112
        {
            public byte BitVector { get; set; }
            public uint FileId { get; set; }

            public static PlayActionParams_V112 ReadData(ByteChunk chunk)
            {
                return new PlayActionParams_V112()
                {
                    BitVector = chunk.ReadByte(),
                    FileId = chunk.ReadUInt32()
                };
            }

            public byte[] WriteData()
            {
                using var memStream = new MemoryStream();
                memStream.Write(ByteParsers.Byte.EncodeValue(BitVector, out _));
                memStream.Write(ByteParsers.UInt32.EncodeValue(FileId, out _));
                return memStream.ToArray();
            }

            public uint GetSize()
            {
                var bitVectorSize = ByteHelper.GetPropertyTypeSize(BitVector);
                var fileIdSize = ByteHelper.GetPropertyTypeSize(FileId);
                return bitVectorSize + fileIdSize;
            }
        }

        // What a SetSwitch action selects: the group, then the value within it. Eight bytes, and
        // reading them is what took the 22 SetSwitch actions in Warhammer III's banks from stopping
        // eight bytes short of their own end to accounting for every byte the bank states.
        public class SwitchActionParams_V112
        {
            public uint SwitchGroupId { get; set; }
            public uint SwitchValueId { get; set; }

            public static SwitchActionParams_V112 ReadData(ByteChunk chunk)
            {
                return new SwitchActionParams_V112()
                {
                    SwitchGroupId = chunk.ReadUInt32(),
                    SwitchValueId = chunk.ReadUInt32()
                };
            }

            public byte[] WriteData()
            {
                using var memStream = new MemoryStream();
                memStream.Write(ByteParsers.UInt32.EncodeValue(SwitchGroupId, out _));
                memStream.Write(ByteParsers.UInt32.EncodeValue(SwitchValueId, out _));
                return memStream.ToArray();
            }

            public uint GetSize()
                => ByteHelper.GetPropertyTypeSize(SwitchGroupId) + ByteHelper.GetPropertyTypeSize(SwitchValueId);
        }

        public class StateActionParams_V112
        {
            public uint StateGroupId { get; set; }
            public uint TargetStateId { get; set; }

            public static StateActionParams_V112 ReadData(ByteChunk chunk)
            {
                return new StateActionParams_V112()
                {
                    StateGroupId = chunk.ReadUInt32(),
                    TargetStateId = chunk.ReadUInt32()
                };
            }

            public byte[] WriteData()
            {
                using var memStream = new MemoryStream();
                memStream.Write(ByteParsers.UInt32.EncodeValue(StateGroupId, out _));
                memStream.Write(ByteParsers.UInt32.EncodeValue(TargetStateId, out _));
                return memStream.ToArray();
            }

            public uint GetSize()
                => ByteHelper.GetPropertyTypeSize(StateGroupId) + ByteHelper.GetPropertyTypeSize(TargetStateId);
        }
    }
}
