using System.Buffers.Binary;
using System.Text;
using Shared.Core.Settings;

namespace Shared.Core.PackFiles.Utility
{
    public static class FileEncryption
    {
        private static readonly byte[] s_iNDEX_STRING_KEY = Encoding.ASCII.GetBytes("#:AhppdV-!PEfz&}[]Nv?6w4guU%dF5.fq:n*-qGuhBJJBm&?2tPy!geW/+k#pG?");
        private const uint INDEX_U32_KEY = 0xE10B_73F4;
        private const ulong DATA_KEY = 0x8FEB_2A67_40A6_920E;

        public static byte[] Decrypt(byte[] ciphertext, GameTypeEnum game)
        {
            // First, make sure the file ends in a multiple of 8. If not, extend it with zeros.
            // We need it because the decoding is done in packs of 8 bytes.
            var size = ciphertext.Length;
            var padding = 8 - size % 8;
            if (padding < 8)
                Array.Resize(ref ciphertext, size + padding);

            // Then decrypt the file in packs of 8 as it's faster than in packs of 4
            var plaintext = new byte[ciphertext.Length];
            ulong edi = 0;
            var chunks = ciphertext.Length / 8;
            var hasPartialChunk = size % 8 != 0;
            var encryptedBlockCount = hasPartialChunk ? chunks - 1 : chunks;
            var keystream = encryptedBlockCount > 0 ? GetKeystream(game) : EncryptionKeystream.None;

            using (var memStream = new MemoryStream(ciphertext))
            using (var reader = new BinaryReader(memStream))
            using (var writer = new BinaryWriter(new MemoryStream(plaintext)))
            {
                for (var i = 0; i < chunks; i++)
                {
                    if (hasPartialChunk && i == chunks - 1)
                    {
                        // A final partial chunk is not encrypted
                        writer.Write(reader.ReadBytes(8));
                    }
                    else
                    {
                        var esi = edi;
                        memStream.Seek((long)esi, SeekOrigin.Begin);
                        var blockKey = GetBlockKey(edi, keystream);
                        var data = reader.ReadUInt64();
                        var plaintextBlock = blockKey ^ data;
                        writer.Seek((int)esi, SeekOrigin.Begin);
                        writer.Write(plaintextBlock);
                    }
                    edi += 8;
                }
            }

            // Remove the extra bytes we added in the first step
            Array.Resize(ref plaintext, size);
            return plaintext;
        }

        public static void DecryptInPlace(Span<byte> buffer, long entrySize, GameTypeEnum game, long entryRelativeOffset = 0)
        {
            // We need it because the decoding is done in packs of 8 bytes
            if (entrySize < 8)
                return;

            var keystream = GetKeystream(game);
            for (var off = 0; off + 8 <= buffer.Length; off += 8)
            {
                var edi = entryRelativeOffset + off;
                if (edi + 8 > entrySize)
                    break;

                var cipher = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(off, 8));
                var plain = GetBlockKey((ulong)edi, keystream) ^ cipher;
                BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(off, 8), plain);
            }
        }

        // This function decrypts the size of a PackedFile
        public static uint DecryptAndReadU32(BinaryReader reader, uint secondKey)
        {
            var ciphertext = reader.ReadUInt32();
            return ciphertext ^ INDEX_U32_KEY ^ ~secondKey;
        }

        // This function decrypts the path of a PackedFile
        public static string DecryptAndReadString(Stream stream, uint secondKey)
        {
            StringBuilder path = new();
            var index = 0;
            while (true)
            {
                var character = stream.ReadByte();
                if (character == -1) break;

                var decryptedChar = (byte)(character ^ s_iNDEX_STRING_KEY[index % s_iNDEX_STRING_KEY.Length] ^ ~secondKey);
                if (decryptedChar == 0) break;

                path.Append((char)decryptedChar);
                index++;
            }
            return path.ToString();
        }

        public static byte[] Encrypt(byte[] plaintext, GameTypeEnum game)
        {
            // Ensure the plaintext is a multiple of 8 bytes by padding with zeros if necessary
            var size = plaintext.Length;
            var padding = 8 - size % 8;
            if (padding < 8)
                Array.Resize(ref plaintext, size + padding);

            var ciphertext = new byte[plaintext.Length];
            ulong edi = 0;
            var chunks = plaintext.Length / 8;
            var hasPartialChunk = size % 8 != 0;
            var encryptedBlockCount = hasPartialChunk ? chunks - 1 : chunks;
            var keystream = encryptedBlockCount > 0 ? GetKeystream(game) : EncryptionKeystream.None;

            using (var memStream = new MemoryStream(plaintext))
            using (var reader = new BinaryReader(memStream))
            using (var writer = new BinaryWriter(new MemoryStream(ciphertext)))
            {
                for (var i = 0; i < chunks; i++)
                {
                    if (hasPartialChunk && i == chunks - 1)
                    {
                        // Do not encrypt a final partial chunk
                        writer.Write(reader.ReadBytes(8));
                    }
                    else
                    {
                        var esi = edi;
                        memStream.Seek((long)esi, SeekOrigin.Begin);
                        var data = reader.ReadUInt64();
                        var encrypted = data ^ GetBlockKey(edi, keystream);
                        writer.Seek((int)esi, SeekOrigin.Begin);
                        writer.Write(encrypted);
                    }
                    edi += 8;
                }
            }

            // Remove extra padding for accurate file representation
            Array.Resize(ref ciphertext, size);
            return ciphertext;
        }

        // This function encrypts a uint32 value (like the PackedFile size)
        public static uint EncryptU32(uint plaintext, uint secondKey)
        {
            return plaintext ^ INDEX_U32_KEY ^ ~secondKey;
        }

        // This function encrypts a file path into the pack file
        public static byte[] EncryptString(string path, uint secondKey)
        {
            var pathBytes = Encoding.ASCII.GetBytes(path);
            // +1 for null terminator
            var encrypted = new byte[pathBytes.Length + 1];
            var index = 0;

            for (var i = 0; i < pathBytes.Length; i++)
            {
                encrypted[i] = (byte)(pathBytes[i] ^ s_iNDEX_STRING_KEY[index % s_iNDEX_STRING_KEY.Length] ^ ~secondKey);
                index++;
            }

            // Null terminator
            encrypted[pathBytes.Length] = 0;
            return encrypted;
        }

        private static ulong GetBlockKey(ulong blockOffset, EncryptionKeystream keystream)
        {
            if (keystream == EncryptionKeystream.ThirtyTwoBitComplement)
                return DATA_KEY * ~(uint)blockOffset;
            else
                return DATA_KEY * ~blockOffset;
        }

        private static EncryptionKeystream GetKeystream(GameTypeEnum game)
        {
            var gameInfo = GameInformationDatabase.GetGameById(game);
            if (gameInfo.EncryptionKeystream is EncryptionKeystream.ThirtyTwoBitComplement or EncryptionKeystream.SixtyFourBitComplement)
                return gameInfo.EncryptionKeystream;

            throw new InvalidOperationException($"{gameInfo.DisplayName} encryption keystream ({gameInfo.EncryptionKeystream}) is unsupported.");
        }

        // File types known to be encrypted in games. Not all files of a type are
        // encrypted, for example encrypted WEM files are normally only music WEMs.
        private static readonly Dictionary<GameTypeEnum, string[]> s_encryptedFileTypesByGame = new()
        {
            [GameTypeEnum.Attila] = [".bnk", ".wem"],
            [GameTypeEnum.Rome2] = [".bnk", ".wem"],
            [GameTypeEnum.Troy] = [".wem"],
            [GameTypeEnum.Warhammer2] = [".wem"],
            [GameTypeEnum.Warhammer3] = [".wem"],
            [GameTypeEnum.Pharaoh] = [".wem", ".dds", ".rigid_model_v2", ".material", ".variantmeshdefinition", ".wsmodel", ".xml"],
        };

        private static readonly HashSet<string> s_encryptedFileTypes = s_encryptedFileTypesByGame.Values
            .SelectMany(extensions => extensions)
            .Append(".txt") // We use .txt in EncryptionTests, it's not actually a file known to be encrypted in games
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // This checks that decrypted bytes look right for their file type.
        // A mismatch usually means the wrong keystream was used to decrypt them.
        public static bool TryValidateDecryptedContent(string path, byte[] bytes, out string? error)
        {
            error = null;
            if (bytes.Length == 0)
                return false;

            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (!s_encryptedFileTypes.Contains(extension))
                return false;

            var expectedMagic = extension switch
            {
                ".bnk" => "BKHD",
                ".wem" => "RIFF",
                ".rigid_model_v2" => "RMV2",
                ".dds" => "DDS ",
                _ => null,
            };

            if (expectedMagic != null)
            {
                if (bytes.Length < expectedMagic.Length)
                    return false;

                var actualMagic = Encoding.ASCII.GetString(bytes, 0, expectedMagic.Length);
                if (actualMagic != expectedMagic)
                {
                    error = $"expected {expectedMagic} header, got {actualMagic}";
                    return true;
                }

                // The magic alone can't tell keystream width apart, since it lives entirely in the low 32
                // bits of the first block, which are identical either way. These fields sit in the high 32
                // bits, which do differ, so they're what actually catches decryption with the wrong keystream.
                if (expectedMagic == "RIFF" && bytes.Length >= 8)
                {
                    var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
                    var expectedChunkSize = bytes.Length - 8;
                    if (chunkSize != expectedChunkSize)
                        error = $"expected RIFF chunk size {expectedChunkSize}, got {chunkSize}";
                }
                else if (extension == ".dds" && bytes.Length >= 8)
                {
                    var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
                    if (headerSize != 124)
                        error = $"expected DDS header size 124, got {headerSize}";
                }

                return true;
            }

            try
            {
                var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿', ' ', '\t', '\r', '\n');
                if (extension != ".txt" && !text.StartsWith('<'))
                    error = $"expected content starting with '<', got \"{text[..Math.Min(text.Length, 20)]}\"";
                else if (text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
                    error = "expected readable text";
            }
            catch (DecoderFallbackException)
            {
                error = "expected valid UTF-8 text";
            }

            return true;
        }
    }
}
