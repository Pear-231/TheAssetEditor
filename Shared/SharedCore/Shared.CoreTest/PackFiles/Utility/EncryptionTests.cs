using System.Text;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Test.TestingUtility.TestUtility;

namespace Shared.CoreTest.PackFiles.Utility
{
    internal class EncryptionTests
    {
        // For these tests we use three packs, one for each pack file version and keystream combination.
        // Each pack contains three text files at the following paths:
        // "I_am_encrypted.txt"
        // "I_am_also_encrypted_but_longer.txt"
        // "folder\I_am_encrypted_and_nested.txt"
        private static readonly Dictionary<GameTypeEnum, string> s_packsByGame = new()
        {
            [GameTypeEnum.Attila] = "EncryptionTests/pfh4_32_bit_keystream.pack",
            [GameTypeEnum.Rome2] = "EncryptionTests/pfh4_32_bit_keystream.pack",
            [GameTypeEnum.Warhammer] = "EncryptionTests/pfh4_64_bit_keystream.pack",
            [GameTypeEnum.Warhammer2] = "EncryptionTests/pfh5_64_bit_keystream.pack",
            [GameTypeEnum.Warhammer3] = "EncryptionTests/pfh5_64_bit_keystream.pack",
            [GameTypeEnum.Troy] = "EncryptionTests/pfh5_64_bit_keystream.pack",
            [GameTypeEnum.Pharaoh] = "EncryptionTests/pfh5_64_bit_keystream.pack",
        };

        private static readonly Dictionary<string, string> s_fileContent = new(StringComparer.OrdinalIgnoreCase)
        {
            ["I_am_encrypted.txt"] = "I am encrypted!",
            ["I_am_also_encrypted_but_longer.txt"] = "I am also encrypted, but longer!",
            ["I_am_encrypted_and_nested.txt"] = "I am encrypted, and nested!",
        };

        [TestCaseSource(nameof(GamesWithFixturePacks))]
        public void Decrypt_UsesGamesKeystream(GameTypeEnum game)
        {
            var sourceContainer = LoadPack(s_packsByGame[game], game);
            var files = sourceContainer.GetAllFiles();

            Assert.That(files, Has.Count.EqualTo(3));

            var originalPlaintext = new Dictionary<string, byte[]>();
            foreach (var entry in files)
            {
                var source = (PackedFileSource)entry.Value.DataSource;
                Assert.That(source.IsEncrypted, Is.True, $"{entry.Key} should be flagged encrypted by the fixture pack's header.");

                var plaintext = source.ReadData();
                var expectedPlaintext = s_fileContent[Path.GetFileName(entry.Key)];
                var plaintext = source.ReadData();
                Assert.That(Encoding.ASCII.GetString(plaintext), Is.EqualTo(expectedPlaintext), $"{entry.Key} plaintext.");

                if (!source.IsCompressed)
                {
                    var ciphertext = ReadCiphertext(source);
                    Assert.That(FileEncryption.Encrypt(plaintext, game), Is.EqualTo(ciphertext), $"{entry.Key} ciphertext.");
            }
            }
        }

        [TestCaseSource(nameof(GamesWithFixturePacks))]
        public void DecryptInPlace_UsesGamesKeystream(GameTypeEnum game)
            {
            var sourceContainer = LoadPack(s_packsByGame[game], game);
            var files = sourceContainer.GetAllFiles();

            foreach (var entry in files)
                {
                    var source = (PackedFileSource)entry.Value.DataSource;
                if (source.IsCompressed)
                    continue;

                var expectedPlaintext = s_fileContent[Path.GetFileName(entry.Key)];
                var buffer = ReadCiphertext(source);

                FileEncryption.DecryptInPlace(buffer, source.Size, game);

                Assert.That(Encoding.ASCII.GetString(buffer), Is.EqualTo(expectedPlaintext), $"{entry.Key}");
            }
        }

        private static Dictionary<GameTypeEnum, string>.KeyCollection GamesWithFixturePacks() => s_packsByGame.Keys;

        private static PackFileContainer LoadPack(string fileName, GameTypeEnum game)
        {
            var path = PathHelper.GetDataFile(fileName);
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            return PackFileSerializerLoader.Load(path, stream.Length, reader, new CustomPackDuplicateFileResolver(), game);
        }

        private static byte[] ReadCiphertext(PackedFileSource source)
        {
            using var stream = File.OpenRead(source.Parent.FilePath);
            stream.Seek(source.Offset, SeekOrigin.Begin);
            var raw = new byte[source.Size];
            stream.ReadExactly(raw);
            return raw;
        }
    }
}
