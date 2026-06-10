using System;
using System.IO;
using System.Text;
using Serilog;
using Shared.Core.ErrorHandling;

namespace Shared.GameFormats.Bmd
{
    // Parses .bmd.vegetation FASTBIN0 files (outer version=2, TreeList version=4).
    // Format reference: Research/rpfm-master/rpfm_lib/src/files/bmd_vegetation/tree_list/v4.rs
    // Strings inside vegetation files use u8 length prefix (not u16 like the main BMD parser).
    public class BmdVegetationParser
    {
        private readonly BinaryReader _reader;
        private readonly Stream _stream;
        private readonly ILogger _logger = Logging.Create<BmdVegetationParser>();

        public BmdVegetationParser(Stream stream)
        {
            _stream = stream;
            _reader = new BinaryReader(stream, Encoding.UTF8);
        }

        public static BmdVegetationFile? TryParse(byte[] data)
        {
            try
            {
                using var stream = new MemoryStream(data);
                var parser = new BmdVegetationParser(stream);
                return parser.Parse();
            }
            catch (Exception ex)
            {
                Logging.Create<BmdVegetationParser>().Warning("Failed to parse vegetation file: {Message}", ex.Message);
                return null;
            }
        }

        public BmdVegetationFile Parse()
        {
            var veg = new BmdVegetationFile();

            var magic = Encoding.UTF8.GetString(_reader.ReadBytes(8));
            if (magic != "FASTBIN0")
                throw new InvalidOperationException($"Expected FASTBIN0 magic, got '{magic}'");

            veg.FastBinVersion = _reader.ReadUInt16();

            veg.TreeListVersion = _reader.ReadUInt16();
            _logger.Here().Information("Vegetation FastBinVersion={FastBin}, TreeListVersion={TL}", veg.FastBinVersion, veg.TreeListVersion);

            if (veg.TreeListVersion != 4)
                throw new NotSupportedException($"Unsupported vegetation TreeList version: {veg.TreeListVersion}");

            ReadTreeListV4(veg);

            veg.GrassListVersion = _reader.ReadUInt16();
            if (veg.GrassListVersion != 4)
                throw new NotSupportedException($"Unsupported vegetation GrassList version: {veg.GrassListVersion}");

            // RPFM documents v4 grass entries as empty records. The count still has to be
            // consumed so the parser remains aligned and validates the complete file.
            veg.GrassGroupCount = _reader.ReadUInt32();

            if (_stream.Position != _stream.Length)
                throw new InvalidDataException($"Vegetation parser stopped at {_stream.Position} of {_stream.Length} bytes.");

            return veg;
        }

        private void ReadTreeListV4(BmdVegetationFile veg)
        {
            var groupCount = _reader.ReadUInt32();
            _logger.Here().Information("Vegetation: {Count} tree model groups", groupCount);

            for (var g = 0; g < groupCount; g++)
            {
                var group = new TreeModelGroup();
                group.ModelPath = ReadStringU8();

                var instanceCount = _reader.ReadUInt32();
                for (var i = 0; i < instanceCount; i++)
                {
                    var instance = new TreeInstance
                    {
                        X = _reader.ReadSingle(),
                        Y = _reader.ReadSingle(),
                        Z = _reader.ReadSingle(),
                    };
                    // rotation is 0-255; multiply by 1.40625 to get degrees (255 * 1.40625 ≈ 358.6°)
                    instance.RotationDegrees = _reader.ReadByte() * 1.40625f;
                    instance.Scale = _reader.ReadSingle();
                    instance.Flags = _reader.ReadByte();
                    group.Instances.Add(instance);
                }

                veg.TreeGroups.Add(group);
                _logger.Here().Information("Tree group '{Path}': {Count} instances", group.ModelPath, group.Instances.Count);
            }
        }

        private string ReadStringU8()
        {
            var length = _reader.ReadByte();
            if (length == 0) return string.Empty;
            return Encoding.UTF8.GetString(_reader.ReadBytes(length));
        }
    }
}
