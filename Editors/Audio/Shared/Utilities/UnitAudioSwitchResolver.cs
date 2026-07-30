using System.IO;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.DB;

namespace Editors.Audio.Shared.Utilities
{
    public interface IUnitAudioSwitchResolver
    {
        IReadOnlyDictionary<string, string> Resolve(GameTypeEnum gameType, string variantMeshName, IEnumerable<string> switchGroups);
    }

    public class UnitAudioSwitchResolver(IPackFileService packFileService, IDbTableQueryService dbTableQueryService) : IUnitAudioSwitchResolver
    {
        private record SwitchLookup(
            GameTypeEnum GameType,
            string SwitchGroup,
            string LandUnitsColumnName,
            string ReferencedTableName,
            string ReferencedTableSwitchValueColumnName);

        private static readonly IReadOnlyCollection<SwitchLookup> s_switchLookups =
            [
                new(
                    GameType: GameTypeEnum.Warhammer3,
                    SwitchGroup: "Generic_Armour_Type",
                    LandUnitsColumnName: "armour",
                    ReferencedTableName: "unit_armour_types_tables",
                    ReferencedTableSwitchValueColumnName: "audio_type"),
                new(
                    GameType: GameTypeEnum.Warhammer3,
                    SwitchGroup: "Generic_Melee_Weapon_Type",
                    LandUnitsColumnName: "primary_melee_weapon",
                    ReferencedTableName: "melee_weapons_tables",
                    ReferencedTableSwitchValueColumnName: "audio_type")
            ];

        private readonly IPackFileService _packFileService = packFileService;
        private readonly IDbTableQueryService _dbTableQueryService = dbTableQueryService;

        public IReadOnlyDictionary<string, string> Resolve(GameTypeEnum gameType, string variantMeshName, IEnumerable<string> switchGroups)
        {
            var requestedLookups = switchGroups
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(group => (Group: group, Lookup: GetSwitchLookup(gameType, group)))
                .Where(x => x.Lookup != null)
                .ToArray();
            if (string.IsNullOrWhiteSpace(variantMeshName) || requestedLookups.Length == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var containers = _packFileService.GetAllPackfileContainers();
            var variants = LoadRows("variants_tables", containers);
            var unitVariants = LoadRows("unit_variants_tables", containers);
            var landUnits = LoadRows("land_units_tables", containers);

            var variantsForMesh = variants
                .Where(x => string.Equals(
                    NormaliseVariantMeshName(x.GetString("variant_filename")),
                    variantMeshName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(x => x.GetString("variant_name"))
                .Where(x => string.IsNullOrWhiteSpace(x) == false)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var landUnitKeys = unitVariants
                .Where(x => variantsForMesh.Contains(x.GetString("variant") ?? ""))
                .Select(x => x.GetString("unit"))
                .Where(x => string.IsNullOrWhiteSpace(x) == false)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var matchingLandUnits = landUnits
                .Where(x => landUnitKeys.Contains(x.GetString("key") ?? ""))
                .ToArray();

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (group, lookup) in requestedLookups)
            {
                var sourceKeys = matchingLandUnits
                    .Select(x => x.GetString(lookup!.LandUnitsColumnName))
                    .Where(x => string.IsNullOrWhiteSpace(x) == false)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var values = LoadRows(lookup.ReferencedTableName, containers)
                    .Where(x => sourceKeys.Contains(x.GetString("key") ?? ""))
                    .Select(x => x.GetString(lookup.ReferencedTableSwitchValueColumnName))
                    .Where(x => string.IsNullOrWhiteSpace(x) == false)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (values.Length != 0)
                    result[group] = string.Join(", ", values);
            }

            return result;
        }

        private static SwitchLookup? GetSwitchLookup(GameTypeEnum gameType, string switchGroup)
        {
            return s_switchLookups.FirstOrDefault(lookup => lookup.GameType == gameType 
                && string.Equals(lookup.SwitchGroup, switchGroup, StringComparison.OrdinalIgnoreCase));
        }

        private DbTableRow[] LoadRows(string tableName, List<IPackFileContainer> containers)
        {
            return _dbTableQueryService.LoadTables(tableName, containers)
                .SelectMany(x => x.Rows)
                .ToArray();
        }

        public static string NormaliseVariantMeshName(string? variantMeshFilename)
        {
            if (string.IsNullOrWhiteSpace(variantMeshFilename))
                return "";

            var filename = Path.GetFileName(variantMeshFilename.Replace('\\', '/'));
            return Path.GetFileNameWithoutExtension(filename);
        }
    }
}
