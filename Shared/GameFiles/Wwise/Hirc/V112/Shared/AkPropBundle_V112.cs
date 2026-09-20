using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc.V112.Shared
{
    public class AkPropBundle_V112
    {
        public byte Props {  get; set; }
        public List<AkPropBundleInstance_V112> PropsList { get; set; } = [];

        public void ReadData(ByteChunk chunk, BankVersion bankVersion)
        {
            Props = chunk.ReadByte();

            // Read all the Ids first
            for (byte i = 0; i < Props; i++)
                PropsList.Add(new AkPropBundleInstance_V112() { Id = bankVersion.DecodePropertyId(chunk.ReadByte()) });
            
            // Then write all the values
            for (byte i = 0; i < Props; i++)
                PropsList[i].Value = chunk.ReadUInt32();
        }

        public byte[] WriteData(BankVersion bankVersion)
        {
            using var memStream = new MemoryStream();
            memStream.Write(ByteParsers.Byte.EncodeValue((byte)PropsList.Count, out _));

            // Write all the Ids first
            foreach (var akProp in PropsList)
                memStream.Write(ByteParsers.Byte.EncodeValue(bankVersion.EncodePropertyId(akProp.Id), out _));

            // Then write all the values
            foreach (var akProp in PropsList)
                memStream.Write(ByteParsers.UInt32.EncodeValue(akProp.Value, out _));

            return memStream.ToArray();
        }

        public uint GetSize()
        {
            var propsSize = ByteHelper.GetPropertyTypeSize(Props);

            uint propsListSize = 0;
            foreach (var akProp in PropsList)
                propsListSize += akProp.GetSize();

            return propsSize + propsListSize;
        }

        public class AkPropBundleInstance_V112
        {
            public AkPropId Id { get; set; }
            public uint Value { get; set; }

            public uint GetSize()
            {
                var idSize = (uint)sizeof(byte);
                var valueSize = ByteHelper.GetPropertyTypeSize(Value);
                return idSize + valueSize;
            }
        }
    }
}
