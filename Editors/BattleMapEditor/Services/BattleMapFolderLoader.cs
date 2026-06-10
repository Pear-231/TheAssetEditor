using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Bmd;

namespace Editors.BattleMapEditor.Services
{
    public class BattleMapLayer
    {
        public string Name { get; init; } = string.Empty;
        public BmdFile BmdFile { get; init; } = null!;
        public PackFile PackFile { get; init; } = null!;
    }

    public class BattleMapResourceEntry
    {
        public string FileName { get; init; } = string.Empty;
        public string PackPath { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public bool IsFound { get; init; }
    }

    public class BattleMapLoadResult
    {
        public PackFile? PrimaryPackFile { get; init; }
        public List<BattleMapLayer> Layers { get; init; } = [];
        public List<BattleMapResourceEntry> Resources { get; init; } = [];
    }

    public class BattleMapFolderLoader
    {
        private readonly IPackFileService _packFileService;

        public BattleMapFolderLoader(IPackFileService packFileService)
        {
            _packFileService = packFileService;
        }

        public BattleMapLoadResult Load(string folderPath)
        {
            var folder = folderPath.TrimEnd('/').TrimEnd('\\').Replace('\\', '/');
            var resources = new List<BattleMapResourceEntry>();
            var layers = new List<BattleMapLayer>();
            PackFile? primaryPackFile = null;

            // Primary BMD
            var primaryPath = $"{folder}/bmd_data.bin";
            var primaryFile = _packFileService.FindFile(primaryPath);
            resources.Add(new BattleMapResourceEntry { FileName = "bmd_data.bin", PackPath = primaryPath, Type = "BMD", IsFound = primaryFile != null });

            if (primaryFile != null)
            {
                primaryPackFile = primaryFile;
                try
                {
                    var bmd = BmdParser.Parse(primaryFile.DataSource.ReadData());
                    layers.Add(new BattleMapLayer { Name = "Main", BmdFile = bmd, PackFile = primaryFile });
                }
                catch (Exception ex)
                {
                    resources.Add(new BattleMapResourceEntry { FileName = "bmd_data.bin (parse error)", PackPath = ex.Message, Type = "Error", IsFound = false });
                }
            }

            // Layer BMDs
            var layerFiles = _packFileService.FindAllWithExtention(".bin")
                .Where(x =>
                {
                    var norm = x.FileName.Replace('\\', '/');
                    var dir = Path.GetDirectoryName(norm)?.Replace('\\', '/') ?? string.Empty;
                    return string.Equals(dir, folder, StringComparison.OrdinalIgnoreCase)
                        && norm.EndsWith("_layer_bmd_data.bin", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            foreach (var (fileName, pack) in layerFiles)
            {
                var normalized = fileName.Replace('\\', '/');
                var layerName = Path.GetFileNameWithoutExtension(normalized);
                const string suffix = "_layer_bmd_data";
                if (layerName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    layerName = layerName[..^suffix.Length];

                resources.Add(new BattleMapResourceEntry { FileName = Path.GetFileName(normalized), PackPath = normalized, Type = "Layer BMD", IsFound = true });

                try
                {
                    var layerBmd = BmdParser.Parse(pack.DataSource.ReadData());
                    layers.Add(new BattleMapLayer { Name = layerName, BmdFile = layerBmd, PackFile = pack });
                }
                catch (Exception ex)
                {
                    resources.Add(new BattleMapResourceEntry { FileName = $"{layerName} (parse error)", PackPath = ex.Message, Type = "Error", IsFound = false });
                }
            }

            // Known companion terrain/logic files
            var expectedFiles = new[]
            {
                ("tile_height_map.compressed_map", "Terrain"),
                ("full_lf_logic_map.compressed_map", "Logic"),
                ("lf_normal.dds", "Texture"),
                ("tile_list.bin", "Tile List"),
                ("environments.csv", "Environment"),
            };

            foreach (var (name, type) in expectedFiles)
            {
                var path = $"{folder}/{name}";
                var file = _packFileService.FindFile(path);
                resources.Add(new BattleMapResourceEntry { FileName = name, PackPath = path, Type = type, IsFound = file != null });
            }

            return new BattleMapLoadResult { PrimaryPackFile = primaryPackFile, Layers = layers, Resources = resources };
        }
    }
}
