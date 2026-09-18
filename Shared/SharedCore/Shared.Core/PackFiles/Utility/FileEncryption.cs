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

        // The block keystream differs between pack generations, and getting it wrong is close to
        // invisible: only the upper 32 bits of the multiplier change, so the low half of every 8-byte
        // block still decodes correctly and only the high half is wrong. Encrypted banks then read back
        // with valid four-character chunk tags, a correct version and correct ids, and nonsense chunk
        // sizes -- which looks far more like an unknown file format than a decryption fault. The RIFF/WAVE
        // magic bytes look right under *either* keystream too, for the same reason -- they sit in the low
        // half of the first block -- so only the size field a few bytes later actually tells the two apart.
        //
        // Which keystream a pack needs is a fact about the game that produced it, not about the pack file
        // itself: measured directly against real packs, Warhammer I and Attila are both PFH4 but need
        // opposite keystreams, and the pack header (including every currently-named PFHFlags bit) is
        // byte-for-byte identical between them. There is nothing in a pack's own bytes to detect this
        // from, and every encrypted file within a given game has been measured to agree with every other
        // (checked across multiple packs per game, never a mix), so it is recorded per game rather than
        // guessed at per pack or per file:
        //   32-bit complement: Attila, Rome II
        //   64-bit complement: Warhammer I, Warhammer II, Warhammer III, Troy, Pharaoh
        //   No encrypted content measured: Three Kingdoms. No CA packs: Rome Remastered.
        // See GameInformationDatabase.EncryptionKeystream and the handoff doc's "Encryption: current
        // state and the open decision" for the full measurement.
        //
        // Because the correct keystream is a fact about the game, not the pack, decryption needs to know
        // which game a pack belongs to -- and that is only known when the caller supplies it. Every path
        // that loads a whole game's pack set (PackFileContainerLoader.CreateFromGameEnum, and this
        // codebase's own corpus audits) knows the game for certain and passes it through. A pack loaded
        // any other way -- importing a single pack file, opening a system folder -- carries whatever game
        // is currently selected in the application's Settings, which is correct only if the user has that
        // setting pointed at the game the pack actually came from. THIS IS UNRESOLVABLE FROM INSIDE THIS
        // CLASS: nothing in a pack's bytes says which game it is, so if the caller's game is wrong (or
        // absent), the wrong keystream is used silently and encrypted content decodes to garbage that
        // looks like corruption rather than a mismatched setting. If you are manually loading a pack from
        // a game other than the one currently selected in Settings, set Settings to that game first.
        //
        // When no game is supplied at all, PFH5 packs fall back to the 64-bit complement and everything
        // else to the 32-bit one. That fallback is only known correct for the six games measured above,
        // and is wrong for Warhammer I in particular (PFH4, but needs 64-bit) -- it exists only so a pack
        // with no game association still decrypts as well as the previous, version-only rule did.
        private static ulong BlockKey(ulong blockOffset, PackFileVersion version, GameTypeEnum? game)
            => ResolveKeystream(version, game) == EncryptionKeystream.ThirtyTwoBitComplement
                ? DATA_KEY * ~(uint)blockOffset
                : DATA_KEY * ~blockOffset;

        private static EncryptionKeystream ResolveKeystream(PackFileVersion version, GameTypeEnum? game)
        {
            if (game.HasValue)
            {
                var gameInfo = GameInformationDatabase.GetGameById(game.Value);
                if (gameInfo.HasKnownKeystream)
                    return gameInfo.EncryptionKeystream;
            }

            return version >= PackFileVersion.PFH5
                ? EncryptionKeystream.SixtyFourBitComplement
                : EncryptionKeystream.ThirtyTwoBitComplement;
        }

        public static byte[] Decrypt(byte[] ciphertext, PackFileVersion version, GameTypeEnum? game = null)
        {
            // First, make sure the file ends in a multiple of 8. If not, extend it with zeros.
            // We need it because the decoding is done in packs of 8 bytes.
            var size = ciphertext.Length;
            var padding = 8 - size % 8;
            if (padding < 8)
                Array.Resize(ref ciphertext, size + padding);

            // Then decrypt the file in packs of 8. It's faster than in packs of 4.
            var plaintext = new byte[ciphertext.Length];
            ulong edi = 0;
            var chunks = ciphertext.Length / 8;

            using (var memStream = new MemoryStream(ciphertext))
            using (var reader = new BinaryReader(memStream))
            using (var writer = new BinaryWriter(new MemoryStream(plaintext)))
            {
                for (var i = 0; i < chunks; i++)
                {
                    if (i == chunks - 1)
                        writer.Write(reader.ReadBytes(8));  // The last chunk is not encrypted.
                    else
                    {
                        var esi = edi;
                        memStream.Seek((long)esi, SeekOrigin.Begin);
                        var prod = BlockKey(edi, version, game);
                        var data = reader.ReadUInt64();
                        prod ^= data;
                        writer.Seek((int)esi, SeekOrigin.Begin);
                        writer.Write(prod);
                    }
                    edi += 8;
                }
            }

            // Remove the extra bytes we added in the first step.
            Array.Resize(ref plaintext, size);
            return plaintext;
        }

        public static void DecryptInPlace(Span<byte> buffer, long entrySize, PackFileVersion version, GameTypeEnum? game = null, long entryRelativeOffset = 0)
        {
            // We need it because the decoding is done in packs of 8 bytes.
            if (entrySize <= 8)
                return;

            for (var off = 0; off + 8 <= buffer.Length; off += 8)
            {
                var edi = entryRelativeOffset + off;
                if (edi + 8 > entrySize - 8)
                    break;

                var cipher = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(off, 8));
                var plain = BlockKey((ulong)edi, version, game) ^ cipher;
                BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(off, 8), plain);
            }
        }

        // This function decrypts the size of a PackedFile.
        public static uint DecryptAndReadU32(BinaryReader reader, uint secondKey)
        {
            var ciphertext = reader.ReadUInt32();
            return ciphertext ^ INDEX_U32_KEY ^ ~secondKey;
        }

        // This function decrypts the path of a PackedFile.
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

        public static byte[] Encrypt(byte[] plaintext, PackFileVersion version, GameTypeEnum? game = null)
        {
            // Ensure the plaintext is a multiple of 8 bytes by padding with zeros if necessary.
            var size = plaintext.Length;
            var padding = 8 - size % 8;
            if (padding < 8)
                Array.Resize(ref plaintext, size + padding);

            var ciphertext = new byte[plaintext.Length];
            ulong edi = 0;
            var chunks = plaintext.Length / 8;

            using (var memStream = new MemoryStream(plaintext))
            using (var reader = new BinaryReader(memStream))
            using (var writer = new BinaryWriter(new MemoryStream(ciphertext)))
            {
                for (var i = 0; i < chunks; i++)
                {
                    if (i == chunks - 1)
                        writer.Write(reader.ReadBytes(8));  // Do not encrypt the last chunk.
                    else
                    {
                        var esi = edi;
                        memStream.Seek((long)esi, SeekOrigin.Begin);
                        var data = reader.ReadUInt64();
                        var encrypted = data ^ BlockKey(edi, version, game);
                        writer.Seek((int)esi, SeekOrigin.Begin);
                        writer.Write(encrypted);
                    }
                    edi += 8;
                }
            }

            // Remove extra padding for accurate file representation.
            Array.Resize(ref ciphertext, size);
            return ciphertext;
        }

        // This function encrypts a uint32 value (like the PackedFile size).
        public static uint EncryptU32(uint plaintext, uint secondKey)
        {
            return plaintext ^ INDEX_U32_KEY ^ ~secondKey;
        }

        // This function encrypts a file path into the pack file.
        public static byte[] EncryptString(string path, uint secondKey)
        {
            var pathBytes = Encoding.ASCII.GetBytes(path);
            var encrypted = new byte[pathBytes.Length + 1];  // +1 for null terminator
            var index = 0;

            for (var i = 0; i < pathBytes.Length; i++)
            {
                encrypted[i] = (byte)(pathBytes[i] ^ s_iNDEX_STRING_KEY[index % s_iNDEX_STRING_KEY.Length] ^ ~secondKey);
                index++;
            }

            encrypted[pathBytes.Length] = 0;  // Null terminator
            return encrypted;
        }
    }
}
