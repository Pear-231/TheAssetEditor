using System.Text;
using Shared.ByteParsing;
using Shared.Core.Settings;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;

namespace Shared.Core.PackFiles.Models.FileSources
{
    public class PackedFileSourceParent
    {
        public required string FilePath { get; set; }

        // Set by a caller that knows for certain which game this pack belongs to -- currently
        // PackFileContainerLoader (all three of its creation paths) and this codebase's own corpus
        // audit tooling. Left null otherwise. Only read by FileEncryption, and only matters for
        // encrypted packs; see FileEncryption.BlockKey for what null means for those.
        public GameTypeEnum? GameType { get; set; }

        private PackFileVersion? _version;

        // Decryption needs the version of the pack the bytes came from, because the block keystream
        // changed between generations. It is a property of the pack file, so it is read from the header
        // here and memoised per pack rather than threaded through every construction site -- the cached
        // container rebuilds sources from a database that has no version column, and would otherwise
        // have to guess. Only encrypted packs ever ask, so a container built over a path that does not
        // exist (as several tests do) never reaches this.
        public PackFileVersion Version => _version ??= ReadVersionFromHeader();

        private PackFileVersion ReadVersionFromHeader()
        {
            using var stream = File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> magic = stackalloc byte[4];
            stream.ReadExactly(magic);
            return PackFileVersionConverter.GetEnum(Encoding.ASCII.GetString(magic));
        }
    }

    public record PackedFileSource : IDataSource
    {
        public long Offset { get; private set; }
        public long Size { get; private set; }
        public bool IsEncrypted { get; private set; }
        public bool IsCompressed { get; set; }
        public CompressionFormat CompressionFormat { get; set; }
        public uint UncompressedSize { get; set; }
        public PackedFileSourceParent Parent { get; set; }

        public PackedFileSource(
            PackedFileSourceParent parent,
            long offset,
            long length,
            bool isEncrypted,
            bool isCompressed,
            CompressionFormat compressionFormat,
            uint uncompressedSize)
        {
            Offset = offset;
            Parent = parent;
            Size = length;
            IsEncrypted = isEncrypted;
            IsCompressed = isCompressed;
            CompressionFormat = compressionFormat;
            UncompressedSize = uncompressedSize;
        }

        public byte[] ReadData()
        {
            using var stream = File.Open(Parent.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return ReadData(stream);
        }

        public byte[] ReadData(Stream knownStream)
        {
            var data = new byte[Size];
            knownStream.Seek(Offset, SeekOrigin.Begin);
            knownStream.ReadExactly(data, 0, (int)Size);

            if (IsEncrypted)
                data = FileEncryption.Decrypt(data, Parent.Version, Parent.GameType);

            if (IsCompressed)
            {
                data = FileCompression.Decompress(data, (int)UncompressedSize, CompressionFormat);
                if (data.Length != UncompressedSize)
                    throw new InvalidDataException($"Decompressed bytes {data.Length:N0} does not match the expected uncompressed bytes {UncompressedSize:N0}.");
            }

            return data;
        }

        public byte[] PeekData(int size)
        {
            byte[] data;

            using (var stream = File.Open(Parent.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Seek(Offset, SeekOrigin.Begin);

                if (!IsEncrypted && !IsCompressed)
                {
                    data = new byte[size];
                    stream.ReadExactly(data);
                }
                else
                {
                    data = new byte[Size];
                    stream.ReadExactly(data);

                    if (IsEncrypted)
                        data = FileEncryption.Decrypt(data, Parent.Version, Parent.GameType);

                    if (IsCompressed)
                    {
                        data = FileCompression.Decompress(data, size, CompressionFormat);
                        if (data.Length != size)
                            throw new InvalidDataException($"Decompressed bytes {data.Length:N0} does not match the expected uncompressed bytes {size:N0}.");
                    }
                }
            }           

            return data;
        }

        public byte[] ReadDataWithoutDecompressing()
        {
            var data = new byte[Size];

            using (var stream = File.Open(Parent.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Seek(Offset, SeekOrigin.Begin);
                stream.ReadExactly(data);
            }

            if (IsEncrypted)
                data = FileEncryption.Decrypt(data, Parent.Version, Parent.GameType);

            return data;
        }

        public ByteChunk ReadDataAsChunk() => new ByteChunk(ReadData());
    }
}
