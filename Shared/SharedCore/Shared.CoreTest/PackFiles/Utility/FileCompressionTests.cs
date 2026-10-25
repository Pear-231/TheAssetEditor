using System.Text;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace Shared.CoreTest.PackFiles.Utility
{
    internal class FileCompressionTests
    {
        private IPackFileService _packFileService;
        private IPackFileContainer _container;

        [SetUp]
        public void Setup()
        {
            var eventHub = new Mock<IGlobalEventHub>();
            _packFileService = new PackFileService(eventHub.Object);
            _container = _packFileService.CreateNewPackFileContainer("EncryptedOutput", PackFileVersion.PFH5, PackFileCAType.MOD, true);

            // Use files that are large enough for compression to be effective as files that are too small may actually increase in size when compressed
            List<NewPackFileEntry> files = [
                new("Directory_0", PackFile.CreateFromASCII("file0.txt", new string('A', 1_024))),
                new("Directory_0", PackFile.CreateFromASCII("file1.txt", new string('B', 2_048))),
                new("Directory_0\\subfolder", PackFile.CreateFromASCII("subfile0.txt", new string('C', 4_096))),
                new("Directory_0\\subfolder", PackFile.CreateFromASCII("subfile1.txt", new string('D', 8_192)))
            ];

            _packFileService.AddFilesToPack(_container, files);
        }

        [Test]
        public void TestCompressAndDecompressPackFile()
        {
            var compressionFormats = Enum.GetValues<CompressionFormat>().Where(compressionFormat => compressionFormat != CompressionFormat.None);
            var originals = _container.GetAllFiles().ToDictionary(file => file.Value.Name, file => file.Value.DataSource.ReadData());

            foreach (var fileName in originals.Keys)
            {
                var data = originals[fileName];

                foreach (var compressionFormat in compressionFormats)
                {
                    var compressedData = FileCompression.Compress(data, compressionFormat);
                    var decompressedData = FileCompression.Decompress(compressedData, data.Length, compressionFormat);
                    Assert.That(decompressedData, Has.Length.EqualTo(data.Length), $"[{compressionFormat}] {fileName} length mismatch");

                    var expectedValue = Encoding.UTF8.GetString(originals[fileName]);
                    var actualValue = Encoding.UTF8.GetString(decompressedData);
                    Assert.That(actualValue, Is.EqualTo(expectedValue), $"[{compressionFormat}] {fileName} content mismatch after round-trip");

                    // Feed back in for the next iteration
                    data = decompressedData;
                }
            }
        }

        [Test]
        public void TestCompressAndDecompressWithoutUncompressedSizePrefix()
        {
            var data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 20_000).Select(i => $"line {i}{Environment.NewLine}")));

            foreach (var compressionFormat in Enum.GetValues<CompressionFormat>().Where(compressionFormat => compressionFormat != CompressionFormat.None))
            {
                var compressedData = FileCompression.Compress(data, compressionFormat, false);
                var decompressedData = FileCompression.Decompress(compressedData, data.Length, compressionFormat, false);

                Assert.That(decompressedData, Is.EqualTo(data), $"[{compressionFormat}] content mismatch after round-trip");
                Assert.That(compressedData, Is.Not.EqualTo(FileCompression.Compress(data, compressionFormat)), $"[{compressionFormat}] still has the uncompressed size prefix");
            }
        }

        [Test]
        public void Compress_Lz4WithoutUncompressedSizePrefix_IsFrameWithContentChecksum()
        {
            var data = Encoding.ASCII.GetBytes(new string('A', 10_000));

            var compressedData = FileCompression.Compress(data, CompressionFormat.Lz4, false);

            // Magic number, then the frame descriptor with linked blocks (bit 5 clear), no content size (bit 3 clear), a content checksum (bit 2) and 64KB blocks
            Assert.That(compressedData.Take(6), Is.EqualTo(new byte[] { 0x04, 0x22, 0x4D, 0x18, 0x44, 0x40 }));
        }

        [TestCase(CompressionFormat.Lz4)]
        [TestCase(CompressionFormat.Zstd)]
        [TestCase(CompressionFormat.Lzma1)]
        public void Decompress_WithoutUncompressedSizePrefix_OutputSizeLargerThanData_Throws(CompressionFormat compressionFormat)
        {
            var data = Encoding.ASCII.GetBytes(new string('A', 1_000));
            var compressedData = FileCompression.Compress(data, compressionFormat, false);

            Assert.That(() => FileCompression.Decompress(compressedData, data.Length + 500, compressionFormat, false), Throws.Exception);
        }

        [TestCase((byte)0, CompressionFormat.None)]
        [TestCase((byte)1, CompressionFormat.Lz4)]
        [TestCase((byte)2, CompressionFormat.Zstd)]
        public void GetCompressionFormatFromId(byte compressionFormatId, CompressionFormat expectedCompressionFormat)
        {
            Assert.That(FileCompression.GetCompressionFormatFromId(compressionFormatId), Is.EqualTo(expectedCompressionFormat));
        }

        [TestCase((byte)3)]
        [TestCase((byte)255)]
        public void GetCompressionFormatFromId_UnknownId_IsNotSupported(byte compressionFormatId)
        {
            Assert.Throws<NotSupportedException>(() => FileCompression.GetCompressionFormatFromId(compressionFormatId));
        }

        [TestCase(CompressionFormat.None, (byte)0)]
        [TestCase(CompressionFormat.Lz4, (byte)1)]
        [TestCase(CompressionFormat.Zstd, (byte)2)]
        public void GetCompressionFormatId(CompressionFormat compressionFormat, byte expectedCompressionFormatId)
        {
            Assert.That(FileCompression.GetCompressionFormatId(compressionFormat), Is.EqualTo(expectedCompressionFormatId));
        }

        [Test]
        public void GetCompressionFormatId_Lzma1_IsNotSupported()
        {
            Assert.Throws<NotSupportedException>(() => FileCompression.GetCompressionFormatId(CompressionFormat.Lzma1));
        }

        [TestCase("folder\\file.dds", CompressionFormat.Lz4)]
        [TestCase("folder\\file.xml", CompressionFormat.Lz4)]
        [TestCase("folder\\file", CompressionFormat.Lz4)]
        [TestCase("db\\table", CompressionFormat.Lz4)]
        [TestCase("folder\\file.wem", CompressionFormat.None)]
        [TestCase("folder\\file.png", CompressionFormat.None)]
        [TestCase("folder\\file.ca_vp8", CompressionFormat.None)]
        [TestCase("folder\\file.compressed_map", CompressionFormat.None)]
        [TestCase("folder\\file.cufglyphs", CompressionFormat.None)]
        public void GetCompressionFormat_Dragonfruit(string fileName, CompressionFormat expectedCompressionFormat)
        {
            var gameInformation = GameInformationDatabase.GetGameById(GameTypeEnum.Dragonfruit);

            Assert.That(FileCompression.GetCompressionFormat(gameInformation, fileName), Is.EqualTo(expectedCompressionFormat));
        }
    }
}
