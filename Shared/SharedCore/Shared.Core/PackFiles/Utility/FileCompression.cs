using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Encoders;
using K4os.Compression.LZ4.Streams;
using SevenZip;
using SevenZip.Compression.LZMA;
using Shared.Core.Settings;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace Shared.Core.PackFiles.Utility
{
    public enum CompressionFormat
    {
        // Dummy variant to disable compression.
        None,

        // Legacy format. Supported by all PFH5 games (all Post-WH2 games).

        // Specifically, Total War games use the Non-Streamed LZMA1 format with the following custom header:
        // | Bytes | Type  | Data                                                                                |
        // | ----- | ----- | ----------------------------------------------------------------------------------- |
        // |  4    | [u32] | Uncompressed size (as u32, max at 4GB).                                             |
        // |  1    | [u8]  | LZMA model properties (lc, lp, pb) in encoded form... I think. Usually it's `0x5D`. |
        // |  4    | [u32] | Dictionary size (as u32)... I think. It's usually `[0x00, 0x00, 0x40, 0x00]`.       |

        // For reference, a normal Non-Streamed LZMA1 header (from the original spec) contains:
        // | Bytes | Type          | Data                                                        |
        // | ----- | ------------- | ----------------------------------------------------------- |
        // |  1    | [u8]          | LZMA model properties (lc, lp, pb) in encoded form.         |
        // |  4    | [u32]         | Dictionary size (32-bit unsigned integer, little-endian).   |
        // |  8    | [prim@u64]    | Uncompressed size (64-bit unsigned integer, little-endian). |

        // This means one has to move the uncompressed size to the correct place in order for a compressed file to be readable,
        // and one has to remove the uncompressed size and prepend it to the file in order for the game to read the compressed file.
        Lzma1,

        // New format introduced in WH3 6.2.
        // This is a standard Lz4 implementation, with the following tweaks:
        // | Bytes | Type      | Data                                          |
        // | ----- | --------- | --------------------------------------------- |
        // |  4    | [u32]     | Uncompressed size (as u32, max at 4GB).       |
        // |  *    | &[[`u8`]] | Lz4 data, starting with the Lz4 Magic Number. |
        Lz4,

        // New format introduced in WH3 6.2.

        // This is a standard Zstd implementation, with the following tweaks:
        // | Bytes | Type      | Data                                            |
        // | ----- | --------- | ----------------------------------------------- |
        // |  4    | [u32]     | Uncompressed size (as u32, max at 4GB).         |
        // |  *    | &[[`u8`]] | Zstd data, starting with the Zstd Magic Number. |

        // By default the Zstd compression is done with the checksum and content size flags enabled.
        Zstd
    }

    public static class FileCompression
    {
        private const byte LzmaPropertiesIdentifier = 0x5D;
        private const uint Lz4MagicNumber = 0x184D_2204;
        private const uint ZstdMagicNumber = 0xfd2f_b528;

        // The file types CA compress in each compression format, and how they handle tables and files without extensions.
        private sealed class CompressionRules
        {
            public List<string> None { get; init; } = [];
            public List<string> Lz4 { get; init; } = [];
            public CompressionFormat Tables { get; init; } = CompressionFormat.None;
            public CompressionFormat FilesWithoutExtension { get; init; } = CompressionFormat.None;
        }

        // CA generally compress file types in specific formats, presumably because they compress better in that format.
        // Sometimes CA compress file types in various formats (though predominantly in one format), presumably by
        // mistake as they use BOB which compresses by folder not file type. We try to replicate that by assigning
        // some file types a specific compression format according to whether they are exclusively compressed
        // in a given  format or predominantly compressed in a given format by CA.
        // Lmza1 is not specified as it's legacy so we only use that for games that support only that format.
        // Zstd is not specified as by default everything not None or Lz4 is Zstd.
        // Tables are not compressed as compressed tables are bugged in older games.
        // These are the rules for the games without their own rules.
        private static readonly CompressionRules s_defaultRules = new()
        {
            None =
            [
                // In CA packs these files are exclusively in this format
                ".bnk",
                ".ca_vp8",
                ".fxc",
                ".hlsl_compiled",
                ".log",
                ".manifest",
                ".wem",

                // In CA packs these files are mostly in this format
                ".rigid_model_v2",
                // Action Events don't play if the .dat file their names are stored in is compressed
                ".dat",

                // How RPFM formats these files
                ".rpfm_reserved",

                // .wav files aren't? in CA packs but probably better not to compress them
                ".wav",
            ],
            Lz4 =
            [
                // In CA packs these files are exclusively in this format
                ".animpack",
                ".collision",
                ".cs2",
                ".exr",
                ".mvscene",
                ".variantmeshdefinition",
                ".wsmodel",
                ".xt",

                // In CA packs these files are mostly in this format
                ".parsed",
            ],
        };

        private static readonly Dictionary<GameTypeEnum, CompressionRules> s_rulesByGame = new()
        {
            // Warhammer 3 compresses tables
            [GameTypeEnum.Warhammer3] = new()
            {
                None = s_defaultRules.None,
                Lz4 = s_defaultRules.Lz4,
                Tables = CompressionFormat.Zstd,
            },

            // Dragonfruit compresses tables and files without extensions, and every file type in packs of the first build
            // apart from the file types in None, which are presumably already compressed formats. The first build has 28
            // exceptions: two dds files in each of the 14 language local.pack files which CA leave uncompressed.
            // No rule explains them as CA compress other dds files of all sizes so we compress them like any other dds.
            [GameTypeEnum.Dragonfruit] = new()
            {
                None = [".ca_vp8", ".compressed_map", ".cufglyphs", ".png", ".wem"],
                Lz4 =
                [
                    ".anim", ".animgraph", ".atlas", ".battle_preferences", ".benchmark", ".bin", ".binconfig", ".bmd",
                    ".bmp", ".bnk", ".bone_inv_trans_mats", ".cco", ".cm", ".collision", ".csc", ".csv", ".cuf", ".curve_set",
                    ".dat", ".dds", ".environment", ".fxc", ".hlsl", ".hlsli", ".index", ".inl", ".json", ".lighting", ".lisp",
                    ".lua", ".material", ".matgroup", ".meta", ".onnx", ".parsed", ".physics", ".prefab", ".py", ".replay",
                    ".rigid_model_v2", ".shader", ".sharedgraph", ".skel", ".spine", ".spkg", ".sql", ".technique", ".tga",
                    ".tile_database_fragment", ".tiles", ".ttf", ".txt", ".variantmeshdefinition", ".veg", ".wsmodel",
                    ".xlsm", ".xml",
                ],
                Tables = CompressionFormat.Lz4,
                FilesWithoutExtension = CompressionFormat.Lz4,
            },
        };

        // From PFH6 the uncompressed size is in the pack's index so the compressed data does not start with it
        public static byte[] Decompress(byte[] data, int outputSize, CompressionFormat compressionFormat, bool hasUncompressedSizePrefix = true)
        {
            using var stream = new MemoryStream(data, false);
            using var reader = new BinaryReader(stream);

            if (hasUncompressedSizePrefix)
            {
                var uncompressedSize = reader.ReadUInt32();
                if (outputSize > uncompressedSize)
                    throw new InvalidDataException($"Output size {outputSize:N0} cannot be greater than the uncompressed size {uncompressedSize:N0}.");
            }

            if (compressionFormat == CompressionFormat.Zstd)
                return DecompressZstd(reader.BaseStream, outputSize);
            if (compressionFormat == CompressionFormat.Lz4)
                return DecompressLz4(reader.BaseStream, outputSize);
            else if (compressionFormat == CompressionFormat.Lzma1)
                return DecompressLzma(reader.BaseStream, outputSize);
            else
                throw new InvalidDataException("Uh oh, the data is either not compressed or has some unknown compression format.");
        }

        private static byte[] DecompressZstd(Stream compressedDataStream, int outputSize)
        {
            var output = new byte[outputSize];
            using var decompressionStream = new DecompressionStream(compressedDataStream);
            ReadExactly(decompressionStream, output, 0, outputSize);
            return output;
        }

        private static byte[] DecompressLz4(Stream compressedDataStream, int outputSize)
        {
            var output = new byte[outputSize];
            using var decompressionStream = new LZ4DecoderStream(compressedDataStream, i => new LZ4ChainDecoder(i.BlockSize, 0));
            ReadExactly(decompressionStream, output, 0, outputSize);
            return output;
        }

        private static byte[] DecompressLzma(Stream stream, int outputSize)
        {
            // Read the property bytes
            var lzmaPropertiesSize = 5;
            var lzmaProperties = new byte[lzmaPropertiesSize];
            ReadExactly(stream, lzmaProperties, 0, lzmaPropertiesSize);

            var remainingInputSize = stream.Length - stream.Position;

            var output = new byte[outputSize];
            using var outputStream = new MemoryStream(output, 0, outputSize, writable: true, publiclyVisible: true);

            var decoder = new Decoder();
            decoder.SetDecoderProperties(lzmaProperties);
            decoder.Code(stream, outputStream, remainingInputSize, outputSize, null);

            if (outputStream.Position != outputSize)
                throw new InvalidDataException($"Expected uncompressed bytes {outputSize:N0} but only received {outputStream.Position:N0} decompressed bytes.");

            return output;
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            var totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                var bytesRead = stream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                if (bytesRead == 0)
                    throw new InvalidDataException($"Requested {count:N0} bytes but only received {totalBytesRead:N0} bytes.");

                totalBytesRead += bytesRead;
            }
        }

        // From PFH6 the uncompressed size is in the pack's index so the compressed data does not start with it
        public static byte[] Compress(byte[] data, CompressionFormat compressionFormat, bool hasUncompressedSizePrefix = true)
        {
            byte[] compressedData;
            if(compressionFormat == CompressionFormat.Zstd)
                compressedData = CompressZstd(data);
            else if(compressionFormat == CompressionFormat.Lz4)
                compressedData = CompressLz4(data, hasUncompressedSizePrefix);
            else if (compressionFormat == CompressionFormat.Lzma1)
                compressedData = CompressLzma1(data);
            else
                throw new InvalidDataException("Uh oh, the data either cannot be compressed or has some unknown compression format.");

            return hasUncompressedSizePrefix ? compressedData : compressedData[sizeof(uint)..];
        }

        private static byte[] CompressZstd(byte[] data)
        {
            using var stream = new MemoryStream();
            stream.Write(BitConverter.GetBytes((uint)data.Length));

            using (var compressor = new CompressionStream(stream, 3, leaveOpen: true))
            {
                compressor.SetParameter(ZSTD_cParameter.ZSTD_c_contentSizeFlag, 1);
                compressor.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
                compressor.SetPledgedSrcSize((ulong)data.Length);
                compressor.Write(data, 0, data.Length);
            }

            return stream.ToArray();
        }


        private static byte[] CompressLz4(byte[] data, bool hasUncompressedSizePrefix)
        {
            using var stream = new MemoryStream();
            stream.Write(BitConverter.GetBytes((uint)data.Length));

            // From PFH6 the frames have a content checksum and match Lz4's high compression level 3
            LZ4EncoderSettings? settings;
            if (hasUncompressedSizePrefix)
                settings = new LZ4EncoderSettings();
            else
                settings = new LZ4EncoderSettings { ContentChecksum = true, CompressionLevel = LZ4Level.L03_HC };

            using (var encoder = LZ4Stream.Encode(stream, settings, leaveOpen: true))
                encoder.Write(data, 0, data.Length);

            return stream.ToArray();
        }

        private static byte[] CompressLzma1(byte[] data)
        {
            using var stream = new MemoryStream();
            stream.Write(BitConverter.GetBytes(data.Length), 0, 4);

            var encoder = new Encoder();
            encoder.SetCoderProperties(
                [
                    CoderPropID.DictionarySize,
                    CoderPropID.PosStateBits,
                    CoderPropID.LitContextBits,
                    CoderPropID.LitPosBits
                ],
                [
                    0x0040_0000,
                    2,
                    3,
                    0
                ]);

            // Read the property bytes
            encoder.WriteCoderProperties(stream);

            // Write the payload
            using var input = new MemoryStream(data, writable: false);
            encoder.Code(input, stream, input.Length, -1, null);

            return stream.ToArray();
        }

        // From PFH6 the index stores each file's compression format as an ID (0 being not compressed). The IDs come from
        // the Dragonfruit executable, which only defines 1 (Lz4) and 2 (Zstd). Any other value falls through to a decoder
        // that looks like Lzma1 but has no ID of its own. Only Lz4 has been seen in packs.
        public static CompressionFormat GetCompressionFormatFromId(byte compressionFormatId)
        {
            return compressionFormatId switch
            {
                0 => CompressionFormat.None,
                1 => CompressionFormat.Lz4,
                2 => CompressionFormat.Zstd,
                _ => throw new NotSupportedException($"Compression format ID {compressionFormatId} is not a known pack index compression format ID."),
            };
        }

        public static byte GetCompressionFormatId(CompressionFormat compressionFormat)
        {
            return compressionFormat switch
            {
                CompressionFormat.None => 0,
                CompressionFormat.Lz4 => 1,
                CompressionFormat.Zstd => 2,
                _ => throw new NotSupportedException($"{compressionFormat} cannot be written to a pack index as the ID used for it has not been verified."),
            };
        }

        public static CompressionFormat GetCompressionFormat(byte[] compressionFormatBytes)
        {
            // Lzma1 is identified by the properties
            if (compressionFormatBytes[0] == LzmaPropertiesIdentifier)
                return CompressionFormat.Lzma1;

            // Zstd and Lz4 are identified by their magic numbers
            var magicNumber = BitConverter.ToUInt32(compressionFormatBytes);
            if (magicNumber == ZstdMagicNumber)
                return CompressionFormat.Zstd;
            else if (magicNumber == Lz4MagicNumber)
                return CompressionFormat.Lz4;
            else
                return CompressionFormat.None;
        }

        public static bool DoesGameSupportCompression(GameInformation gameInformation)
        {
            var doesGameFilesSupportCompression = !(gameInformation.CompressionFormats.Count == 1 && gameInformation.CompressionFormats.First() == CompressionFormat.None);
            return doesGameFilesSupportCompression;
        }

        public static CompressionFormat GetCompressionFormat(GameInformation gameInformation, string fileName)
        {
            var extension = Path.GetExtension(fileName);
            var firstFilePathPart = fileName.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).First();

            var compressionFormats = gameInformation.CompressionFormats;
            var rules = s_rulesByGame.GetValueOrDefault(gameInformation.Type, s_defaultRules);

            // Check if the game supports any compression at all
            if (compressionFormats.All(compressionFormat => compressionFormat == CompressionFormat.None))
                return CompressionFormat.None;

            // We use the root folder for db tables because they don't have an extension
            var isTable = firstFilePathPart == "db" || extension == ".loc";
            var hasExtension = !string.IsNullOrEmpty(extension);

            if (isTable)
                return rules.Tables;

            if (!hasExtension)
                return rules.FilesWithoutExtension;

            // Anything that isn't preferrably None, Lzma1, or Lz4 is set to Zstd unless the game doesn't support that in which case use None
            // Lzma1 is a legacy format so only use it if it's all the game can use even though games with other formats can use it
            if (rules.None.Contains(extension))
                return CompressionFormat.None;
            else if (compressionFormats.Count == 1 && compressionFormats.Contains(CompressionFormat.Lzma1))
                return CompressionFormat.Lzma1;
            else if (rules.Lz4.Contains(extension) && compressionFormats.Contains(CompressionFormat.Lz4))
                return CompressionFormat.Lz4;
            else if (compressionFormats.Contains(CompressionFormat.Zstd))
                return CompressionFormat.Zstd;
            else
                return CompressionFormat.None;
        }
    }
}
