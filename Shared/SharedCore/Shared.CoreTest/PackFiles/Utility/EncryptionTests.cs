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

        [TestCaseSource(nameof(GetGamesWithKnownKeystream))]
        public void DecryptEncryptPacks(GameTypeEnum game)
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
                Assert.That(Encoding.ASCII.GetString(plaintext), Is.EqualTo(expectedPlaintext), $"{entry.Key} plaintext.");

                originalPlaintext[entry.Key] = plaintext;
            }

            var outputPath = Path.Combine(Path.GetTempPath(), $"EncryptionResave_{game}_{Guid.NewGuid():N}.pack");
            try
            {
                var gameInfo = GameInformationDatabase.GetGameById(game);
                sourceContainer.SaveToDisk(outputPath, false, gameInfo);

                using var reloadStream = File.OpenRead(outputPath);
                using var reloadReader = new BinaryReader(reloadStream);
                var reloadedContainer = PackFileSerializerLoader.Load(outputPath, reloadStream.Length, reloadReader, new CustomPackDuplicateFileResolver(), game);
                var reloadedFiles = reloadedContainer.GetAllFiles().ToList();

                Assert.That(reloadedFiles, Has.Count.EqualTo(originalPlaintext.Count));
                foreach (var entry in reloadedFiles)
                {
                    var source = (PackedFileSource)entry.Value.DataSource;
                    Assert.That(source.IsEncrypted, Is.True, $"{entry.Key} should still be encrypted after a full resave.");

                    var roundTrippedPlaintext = source.ReadData();
                    Assert.That(roundTrippedPlaintext, Is.EqualTo(originalPlaintext[entry.Key]), $"{entry.Key} did not survive a full pack resave.");
                }
            }
            finally
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
        }

        // With no game supplied we don't know for certain what bit keystream a pack requires so we guess using
        // the rules PFH4 = 32-bit, PFH5 = 64-bit. Those rules work for every supported game except Warhammer I
        // which uses PFH4 but 64-bit. So it is a known issue that when loading an encrypted WH1 pack manually,
        // i.e. without AssetEditor doing so itself via the current game setting, decryption will fail.
        [TestCaseSource(nameof(GetGamesWithKnownKeystream))]
        public void CheckDecryptFallback(GameTypeEnum game)
        {
            var container = LoadPack(s_packsByGame[game]);
            var firstFile = container.GetAllFiles().First();
            var source = (PackedFileSource)firstFile.Value.DataSource;

            var decrypted = FileEncryption.Decrypt(ReadCiphertext(source), container.Header.Version);
            var plaintext = source.IsCompressed
                ? FileCompression.Decompress(decrypted, (int)source.UncompressedSize, source.CompressionFormat)
                : decrypted;
            var decryptedText = Encoding.ASCII.GetString(plaintext);
            var expectedText = s_fileContent[Path.GetFileName(firstFile.Key)];

            if (game == GameTypeEnum.Warhammer)
                Assert.That(decryptedText, Is.Not.EqualTo(expectedText), $"{game}: fallback picks the wrong (32-bit) keystream.");
            else
                Assert.That(decryptedText, Is.EqualTo(expectedText), $"{game}: fallback picks the correct keystream.");
        }

        private static IEnumerable<GameTypeEnum> GetGamesWithKnownKeystream()
            => GameInformationDatabase.Games.Values
                .Where(game => game.HasKnownKeystream)
                .Select(game => game.Type);

        private static PackFileContainer LoadPack(string fileName, GameTypeEnum? game = null)
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
            var bytes = new byte[source.Size];
            stream.ReadExactly(bytes);
            return bytes;
        }
    }
}
