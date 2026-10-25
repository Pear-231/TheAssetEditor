using System.Text;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace Shared.CoreTest.PackFiles.Serialization
{
    // Tests the PFH6, PFH7 and PFH8 index layouts using packs built independently of the pack writer
    internal class PackFileSerializerLoaderTests
    {
        private const int PageSize = 4096;
        private const byte Lz4Value = 1;
        private const byte ZstdValue = 2;

        private string _tempDirectory;

        [SetUp]
        public void Setup()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "AE_PackLoaderTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        [TestCase(PackFileVersion.PFH6)]
        [TestCase(PackFileVersion.PFH7)]
        [TestCase(PackFileVersion.PFH8)]
        public void Load_UncompressedAndCompressedFiles_ReadsBackOriginalData(PackFileVersion version)
        {
            var plainData = Encoding.ASCII.GetBytes("plain file");
            var compressedData = Encoding.ASCII.GetBytes(new string('A', 5_000));
            var entries = new List<TestEntry>
            {
                new("folder\\plain.txt", plainData, plainData),
                new("folder\\compressed.txt", FileCompression.Compress(compressedData, CompressionFormat.Lz4, false), compressedData, Lz4Value),
            };

            var container = WriteAndLoad(version, entries);

            AssertFile(container, "folder\\plain.txt", plainData, CompressionFormat.None);
            AssertFile(container, "folder\\compressed.txt", compressedData, CompressionFormat.Lz4);
        }

        [TestCase(Lz4Value, CompressionFormat.Lz4)]
        [TestCase(ZstdValue, CompressionFormat.Zstd)]
        public void Load_CompressionValue_SetsCompressionFormatAndReadsBackOriginalData(byte compressionValue, CompressionFormat expectedFormat)
        {
            var data = Encoding.ASCII.GetBytes(new string('B', 10_000));
            var compressedData = FileCompression.Compress(data, expectedFormat, false);

            var container = WriteAndLoad(PackFileVersion.PFH6, [new TestEntry("compressed.txt", compressedData, data, compressionValue)]);

            AssertFile(container, "compressed.txt", data, expectedFormat);
            var source = (PackedFileSource)container.FindFile("compressed.txt")!.DataSource;
            Assert.That(source.UncompressedSize, Is.EqualTo((uint)data.Length));
        }

        [TestCase(PackFileVersion.PFH8)]
        public void Load_Padding_OffsetsIncludePadding(PackFileVersion version)
        {
            var first = Encoding.ASCII.GetBytes("first");
            var second = Encoding.ASCII.GetBytes("second");
            var third = Encoding.ASCII.GetBytes("third");
            var entries = new List<TestEntry>
            {
                new("first.txt", first, first),
                new("second.txt", second, second, PadToPage: true),
                new("third.txt", third, third),
            };

            var container = WriteAndLoad(version, entries);

            AssertFile(container, "first.txt", first, CompressionFormat.None);
            AssertFile(container, "second.txt", second, CompressionFormat.None);
            AssertFile(container, "third.txt", third, CompressionFormat.None);
            Assert.That(((PackedFileSource)container.FindFile("second.txt")!.DataSource).Offset % PageSize, Is.EqualTo(0));
        }

        [TestCase(PackFileVersion.PFH7)]
        [TestCase(PackFileVersion.PFH8)]
        public void Load_Alignment_AlignsOffset(PackFileVersion version)
        {
            var first = Encoding.ASCII.GetBytes("first");
            var second = Encoding.ASCII.GetBytes("second");
            var third = Encoding.ASCII.GetBytes("third");
            var entries = new List<TestEntry>
            {
                new("first.txt", first, first),
                new("second.txt", second, second, ByteAlignment: 16),
                new("third.txt", third, third),
            };

            var container = WriteAndLoad(version, entries);

            AssertFile(container, "first.txt", first, CompressionFormat.None);
            AssertFile(container, "second.txt", second, CompressionFormat.None);
            AssertFile(container, "third.txt", third, CompressionFormat.None);
            Assert.That(((PackedFileSource)container.FindFile("second.txt")!.DataSource).Offset % 16, Is.EqualTo(0));
        }

        [Test]
        public void Load_AlignmentAndPadding_AlignsOffsetBeforeApplyingPadding()
        {
            var first = Encoding.ASCII.GetBytes("first");
            var second = Encoding.ASCII.GetBytes("second");
            var third = Encoding.ASCII.GetBytes("third");
            var entries = new List<TestEntry>
            {
                new("first.txt", first, first),
                new("second.txt", second, second, ByteAlignment: 16, PadToPage: true),
                new("third.txt", third, third),
            };

            var container = WriteAndLoad(PackFileVersion.PFH8, entries);

            AssertFile(container, "first.txt", first, CompressionFormat.None);
            AssertFile(container, "second.txt", second, CompressionFormat.None);
            AssertFile(container, "third.txt", third, CompressionFormat.None);
            Assert.That(((PackedFileSource)container.FindFile("second.txt")!.DataSource).Offset % PageSize, Is.EqualTo(0));
        }

        [TestCase(PackFileVersion.PFH6)]
        [TestCase(PackFileVersion.PFH7)]
        [TestCase(PackFileVersion.PFH8)]
        public void ReadHeader_ReadsVersionFileCountAndDataStart(PackFileVersion version)
        {
            var data = Encoding.ASCII.GetBytes("data");
            var packBytes = BuildPack(version, [new TestEntry("a.txt", data, data)], out var dataStart);

            using var stream = new MemoryStream(packBytes);
            using var reader = new BinaryReader(stream);
            var header = PackFileSerializerLoader.ReadHeader(reader);

            Assert.That(header.Version, Is.EqualTo(version));
            Assert.That(header.FileCount, Is.EqualTo(1));
            Assert.That(header.DataStart, Is.EqualTo(dataStart));
        }

        private PackFileContainer WriteAndLoad(PackFileVersion version, List<TestEntry> entries)
        {
            var packBytes = BuildPack(version, entries, out _);
            var packPath = Path.Combine(_tempDirectory, "test.pack");
            File.WriteAllBytes(packPath, packBytes);

            using var stream = File.OpenRead(packPath);
            using var reader = new BinaryReader(stream, Encoding.ASCII);
            return PackFileSerializerLoader.Load(packPath, packBytes.Length, reader, new CaPackDuplicateFileResolver(), GameTypeEnum.Dragonfruit);
        }

        private static void AssertFile(PackFileContainer container, string path, byte[] expectedData, CompressionFormat expectedFormat)
        {
            var packFile = container.FindFile(path);
            Assert.That(packFile, Is.Not.Null, path);

            var source = (PackedFileSource)packFile!.DataSource;
            Assert.That(source.CompressionFormat, Is.EqualTo(expectedFormat), path);
            Assert.That(source.IsCompressed, Is.EqualTo(expectedFormat != CompressionFormat.None), path);
            Assert.That(source.ReadData(), Is.EqualTo(expectedData), path);
        }

        private static byte[] BuildPack(PackFileVersion version, List<TestEntry> entries, out long dataStart)
        {
            var index = BuildIndex(version, entries, out var indexSize);
            dataStart = 28 + indexSize;

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes(PackFileVersionConverter.ToString(version)));
            writer.Write(1);                        // Release
            writer.Write(0u);                       // Reference file count
            writer.Write(0u);                       // Reference file index size
            writer.Write((uint)entries.Count);
            writer.Write((uint)indexSize);
            writer.Write(0u);                       // Timestamp
            writer.Write(index);

            foreach (var entry in entries)
            {
                var (alignmentGap, padding) = GetGaps(version, stream.Position, entry);
                writer.Write(new byte[alignmentGap + padding]);
                writer.Write(entry.StoredData);
            }

            return stream.ToArray();
        }

        private static byte[] BuildIndex(PackFileVersion version, List<TestEntry> entries, out int indexSize)
        {
            // The padding depends on the data start, which depends on the index size, so measure the index first
            indexSize = WriteIndex(version, entries, 0).Length;
            return WriteIndex(version, entries, 28 + indexSize);
        }

        private static byte[] WriteIndex(PackFileVersion version, List<TestEntry> entries, long dataStart)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            var position = dataStart;
            foreach (var entry in entries)
            {
                var (alignmentGap, padding) = GetGaps(version, position, entry);
                writer.Write((uint)entry.StoredData.Length);
                if (version >= PackFileVersion.PFH7)
                    writer.Write((byte)entry.ByteAlignment);
                if (version >= PackFileVersion.PFH8)
                    writer.Write((ushort)padding);

                writer.Write(entry.Compression);
                if (entry.Compression != 0)
                    writer.Write((uint)entry.Data.Length);

                writer.Write(Encoding.ASCII.GetBytes(entry.Path));
                writer.Write((byte)0);

                position += alignmentGap + padding + entry.StoredData.Length;
            }

            return stream.ToArray();
        }

        // Mirrors the game: the offset is aligned to the byte alignment first, then the padding is added to it
        private static (int AlignmentGap, int Padding) GetGaps(PackFileVersion version, long position, TestEntry entry)
        {
            var alignment = version >= PackFileVersion.PFH7 ? entry.ByteAlignment : 0;
            var alignedPosition = alignment == 0 ? position : (position + alignment - 1) / alignment * alignment;
            var padding = version >= PackFileVersion.PFH8 && entry.PadToPage ? (int)((PageSize - alignedPosition % PageSize) % PageSize) : 0;
            return ((int)(alignedPosition - position), padding);
        }

        private record TestEntry(
            string Path,
            byte[] StoredData,
            byte[] Data,
            byte Compression = 0,
            int ByteAlignment = 0,
            bool PadToPage = false);
    }
}
