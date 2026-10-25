using Shared.ByteParsing;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace Shared.Core.PackFiles.Serialization
{
    public static class PackFileVersionConverter
    {
        readonly static List<(PackFileVersion EnumValue, string StringValue)> s_values =
        [
            (PackFileVersion.PFH0,  "PFH0"),
            (PackFileVersion.PFH2,  "PFH2"),
            (PackFileVersion.PFH3,  "PFH3"),
            (PackFileVersion.PFH4,  "PFH4"),
            (PackFileVersion.PFH5,  "PFH5"),
            (PackFileVersion.PFH6,  "PFH6"),
            (PackFileVersion.PFH7,  "PFH7"),
            (PackFileVersion.PFH8,  "PFH8")
        ];

        public static string ToString(PackFileVersion versionEnum) => s_values.First(x => x.EnumValue == versionEnum).StringValue;
        public static PackFileVersion GetEnum(string versionStr) => s_values.First(x => x.StringValue == versionStr.ToUpper()).EnumValue;
    }

    public static class PackFileSerializerLoader
    {
        static readonly ILogger s_logger = Logging.CreateStatic(typeof(PackFileSerializerLoader));

        internal static PackFileContainer Load(string packFileSystemPath, long packFileSize, BinaryReader reader, IDuplicateFileResolver duplicatePackFileResolver, GameTypeEnum game)
        {
            try
            {
                var fileNameBuffer = new byte[1024];
                var name = Path.GetFileNameWithoutExtension(packFileSystemPath);
                var header = ReadHeader(reader);
                var output = PackFileContainer.CreatePackFile(name, packFileSystemPath, header);
                output.OriginalLoadByteSize = packFileSize;

                // If larger then int.max throw error
                if (output.Header.FileCount > int.MaxValue)
                    throw new Exception("Too many files in packfile!");

                output.FileList = new Dictionary<string, PackFile>((int)output.Header.FileCount);

                var packedFileSourceParent = new PackedFileSourceParent()
                {
                    FilePath = packFileSystemPath,
                    GameType = game,
                };

                var offset = output.Header.DataStart;
                var headerVersion = output.Header.Version;
                for (var i = 0; i < output.Header.FileCount; i++)
                {
                    uint size;
                    if (output.Header.HasEncryptedIndex)
                        size = FileEncryption.DecryptAndReadU32(reader, (uint)(output.Header.FileCount - i - 1));
                    else
                        size = reader.ReadUInt32();

                    if (output.Header.HasIndexWithTimeStamp)
                        reader.ReadUInt32();


                    // Logic for PFH6, 7 and 8 was found in the pack file index parser in Dragonfruit's executable.
                    // From PFH7 the data's offset is rounded up to a multiple of this if it is not 0
                    byte alignment = 0;
                    if (headerVersion >= PackFileVersion.PFH7)
                        alignment = reader.ReadByte();

                    // In PFH8 padding is added to the data's offset after it has been aligned
                    ushort padding = 0;
                    if (headerVersion >= PackFileVersion.PFH8)
                        padding = reader.ReadUInt16();

                    // In PFH5 this is a generic flag for compression.
                    // From PFH6 this is the compression format itself.
                    byte compressionFormatId = 0;
                    if (headerVersion >= PackFileVersion.PFH5)
                        compressionFormatId = reader.ReadByte();

                    var isCompressed = compressionFormatId != 0;

                    // From PFH6 this is only present if the file is compressed and the compressed data does not start with it as it does before PFH6
                    uint uncompressedSize = 0;
                    if (isCompressed && headerVersion >= PackFileVersion.PFH6)
                        uncompressedSize = reader.ReadUInt32();

                    var fullPackedFileName = IOFunctions.ReadZeroTerminatedAscii(reader, fileNameBuffer).ToLower();
                    var packFileName = Path.GetFileName(fullPackedFileName);
                    var isEncrypted = output.Header.HasEncryptedData;

                    if (alignment != 0)
                        offset = (offset + alignment - 1) / alignment * alignment;

                    offset += padding;

                    var compressionFormat = CompressionFormat.None;
                    if (isCompressed && headerVersion >= PackFileVersion.PFH6)
                        compressionFormat = FileCompression.GetCompressionFormatFromId(compressionFormatId);
                    else if (isCompressed)
                    {
                        var fileHeader = DetectCompressionInfo(reader, offset, size, isEncrypted, game);
                        using var compressionStream = new MemoryStream(fileHeader, false);
                        using var compressionReader = new BinaryReader(compressionStream);
                        uncompressedSize = compressionReader.ReadUInt32();
                        var compressionFormatBytes = compressionReader.ReadBytes(4);
                        compressionFormat = FileCompression.GetCompressionFormat(compressionFormatBytes);
                    }

                    var packedFileSource = new PackedFileSource(packedFileSourceParent, offset, size, isEncrypted, isCompressed, compressionFormat, uncompressedSize);
                    var fileContent = new PackFile(packFileName, packedFileSource);

                    if (duplicatePackFileResolver.CheckForDuplicates)
                    {
                        var containsKey = output.ContainsFile(fullPackedFileName);
                        if (containsKey)
                        {
                            if (duplicatePackFileResolver.KeepDuplicateFile(fullPackedFileName))
                            {
                                s_logger.Here().Warning($"Duplicate file found {fullPackedFileName}");
                                output.AddOrUpdateFile(fullPackedFileName + Guid.NewGuid().ToString(), fileContent);
                            }
                        }
                        else
                            output.AddOrUpdateFile(fullPackedFileName, fileContent);
                    }
                    else
                        output.AddOrUpdateFile(fullPackedFileName, fileContent);

                    offset += size;
                }

                return output;
            }
            catch (Exception e)
            {
                s_logger.Here().Error($"Failed to load packfile {packFileSystemPath} - {e.Message}");
                throw;
            }
        }

        public static PFHeader ReadHeader(BinaryReader reader)
        {
            var fileNameBuffer = new byte[1024];

            var header_strVersion = new string(reader.ReadChars(4));           // 4
            var header_byteMask = reader.ReadInt32();                          // 8
            var header_referenceFileCount = reader.ReadUInt32();               // 12  
            var header = new PFHeader(header_strVersion, header_byteMask, header_referenceFileCount);

            var pack_file_index_size = reader.ReadUInt32();         // 16
            var pack_file_count = reader.ReadUInt32();              // 20
            var packed_file_index_size = reader.ReadUInt32();       // 24

            if (header.HasEncryptedIndex)
            {
                var filesRemaining = header.ReferenceFileCount;
                packed_file_index_size = FileEncryption.DecryptAndReadU32(reader, filesRemaining);
            }

            // Read the buffer of data stuff
            if (header.Version == PackFileVersion.PFH0)
                header.Buffer = new byte[0];
            else if (header.Version == PackFileVersion.PFH2 || header.Version == PackFileVersion.PFH3)
            {
                var timestamp = reader.ReadBytes(8);
                header.Buffer = timestamp;
            }
            else if (header.Version >= PackFileVersion.PFH4)
            {
                // When HasExtendedHeader is set in PFH4 packs RPFM reads 20 more bytes. I've checked all packs in
                // all PFH4 games (Warhammer, Attila, Rome II, Thrones of Britannia), and none have HasExtendedHeader
                // set so have not been able to verify this so commenting it out.
                // When HasExtendedHeader is set in PFH5 packs RPFM does not read any extra bytes. I've checked all
                // packs in all PFH5 games (Warhammer II, Warhammer III, Three Kingdoms, Troy, Pharaoh) and the only
                // pack where HasExtendedHeader is set is Pharaoh's data_special.pack. In this pack, weirdly there are
                // no extra bytes in the header so commenting it out.
                // if (header.HasExtendedHeader)
                //     header.Buffer = reader.ReadBytes(24);
                // else
                //     header.Buffer = reader.ReadBytes(4);
                var timestamp = reader.ReadBytes(4);
                header.Buffer = timestamp;
            }
            else
                throw new Exception($"Unknown packfile type {header.PackFileType}");

            for (var i = 0; i < header.ReferenceFileCount; i++)
                header.DependantFiles.Add(IOFunctions.ReadZeroTerminatedAscii(reader, fileNameBuffer));

            header.DataStart = 24 + (uint)header.Buffer.Length + pack_file_index_size + packed_file_index_size;
            header.FileCount = pack_file_count;

            return header;
        }

        private static byte[] DetectCompressionInfo(BinaryReader reader, long dataOffset, uint entrySize, bool isEncrypted, GameTypeEnum game)
        {
            if (entrySize <= 8 || !isEncrypted && entrySize == 0)
                return [];

            var headerLen = 8;
            var header = new byte[headerLen];

            var savedPos = reader.BaseStream.Position;

            reader.BaseStream.Seek(dataOffset, SeekOrigin.Begin);
            reader.Read(header, 0, headerLen);
            reader.BaseStream.Seek(savedPos, SeekOrigin.Begin);

            if (isEncrypted)
                FileEncryption.DecryptInPlace(header, entrySize, game: game);

            return header;
        }
    }
}
