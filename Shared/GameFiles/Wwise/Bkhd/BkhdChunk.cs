﻿using Shared.Core.ByteParsing;
using Shared.GameFormats.Wwise.Enums;

namespace Shared.GameFormats.Wwise.Bkhd
{
    public class BkhdChunk
    {
        public string OwnerFile { get; set; }
        public BnkChunkHeader ChunkHeader { get; set; } = new BnkChunkHeader() { Tag = BankChunkTypes.BKHD, ChunkSize = 0x18 };
        public AkBankHeader AkBankHeader { get; set; } = new AkBankHeader();
    }

    public class AkBankHeader
    {
        public uint DwBankGeneratorVersion { get; set; }
        public uint DwSoundBankId { get; set; }
        public uint DwLanguageId { get; set; }
        public uint BFeedbackInBank { get; set; }
        public uint DwProjectId { get; set; }
        public byte[] Padding { get; set; }

        public void ReadData(ByteChunk chunk, uint chunkSize)
        {
            DwBankGeneratorVersion = chunk.ReadUInt32();
            DwSoundBankId = chunk.ReadUInt32();
            DwLanguageId = chunk.ReadUInt32();
            BFeedbackInBank = chunk.ReadUInt32();
            DwProjectId = chunk.ReadUInt32();

            var headerDiff = (int)chunkSize - 20;
            if (headerDiff > 0)
                Padding = chunk.ReadBytes(headerDiff);
        }

        public byte[] WriteData()
        {
            using var memStream = new MemoryStream();

            memStream.Write(ByteParsers.UInt32.EncodeValue(DwBankGeneratorVersion, out _));
            memStream.Write(ByteParsers.UInt32.EncodeValue(DwSoundBankId, out _));
            memStream.Write(ByteParsers.UInt32.EncodeValue(DwLanguageId, out _));
            memStream.Write(ByteParsers.UInt32.EncodeValue(BFeedbackInBank, out _));
            memStream.Write(ByteParsers.UInt32.EncodeValue(DwProjectId, out _));

            if (Padding != null && Padding.Length > 0)
                memStream.Write(Padding, 0, Padding.Length);

            return memStream.ToArray();
        }
    }
}
