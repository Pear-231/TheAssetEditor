using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc.V112
{
    public class CAkEvent_V112 : HircItem, ICAkEvent
    {
        public uint ActionListSize { get; set; }
        public List<Action_V112> Actions { get; set; } = [];

        protected override void ReadData(ByteChunk chunk, BankVersion bankVersion)
        {
            ActionListSize = chunk.ReadUInt32();
            for (var i = 0; i < ActionListSize; i++)
            {
                var action = new Action_V112();
                action.ReadData(chunk);
                Actions.Add(action);
            }
        }

        public override byte[] WriteData(BankVersion bankVersion)
        {
            using var memStream = WriteHeader();
            memStream.Write(ByteParsers.UInt32.EncodeValue(ActionListSize, out _));
            foreach (var action in Actions)
                memStream.Write(action.WriteData());

            var byteArray = memStream.ToArray();

            // Reload the object to ensure sanity
            var sanityReload = new CAkEvent_V112();
            var chunk = new ByteChunk(byteArray);
            sanityReload.ReadHirc(chunk, bankVersion);

            return byteArray;
        }

        public override void UpdateSectionSize()
        {
            var idSize = ByteHelper.GetPropertyTypeSize(Id);
            var actionListSizeSize = ByteHelper.GetPropertyTypeSize(ActionListSize);

            uint actionListSize = 0;
            foreach (var action in Actions)
                actionListSize += action.GetSize();

            SectionSize = idSize + actionListSizeSize + actionListSize;
        }

        public List<uint> GetActionIds() => Actions.Select(x => x.ActionId).ToList();

        public class Action_V112
        {
            public uint ActionId { get; set; }

            public void ReadData(ByteChunk chunk)
            {
                ActionId = chunk.ReadUInt32();
            }

            public byte[] WriteData()
            {
                using var memStream = new MemoryStream();
                memStream.Write(ByteParsers.UInt32.EncodeValue(ActionId, out _));
                return memStream.ToArray();
            }

            public uint GetSize()
            {
                var actionIdSize = ByteHelper.GetPropertyTypeSize(ActionId);
                return actionIdSize;
            }
        }
    }
}
