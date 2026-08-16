using Shared.Core.PackFiles;
using Shared.Core.Settings;
using Shared.GameFormats.DB;

namespace Editors.Audio.Shared.Utilities
{
    public sealed class UnitAudioSwitchResolver(IPackFileService packFileService, IDbTableQueryService dbTableQueryService)
    {
        private record SwitchLookup(
            GameTypeEnum GameType,
            string SwitchGroupName,
            string LandUnitsColumnName,
            string ReferencedTableName,
            string ReferencedTableSwitchValueColumnName);

        private static readonly IReadOnlyCollection<SwitchLookup> s_switchLookups =
            [
                new(
                    GameType: GameTypeEnum.Warhammer3,
                    SwitchGroupName: "Generic_Armour_Type",
                    LandUnitsColumnName: "armour",
                    ReferencedTableName: "unit_armour_types_tables",
                    ReferencedTableSwitchValueColumnName: "audio_type"),
                new(
                    GameType: GameTypeEnum.Warhammer3,
                    SwitchGroupName: "Generic_Melee_Weapon_Type",
                    LandUnitsColumnName: "primary_melee_weapon",
                    ReferencedTableName: "melee_weapons_tables",
                    ReferencedTableSwitchValueColumnName: "audio_type")
            ];

        private readonly IPackFileService _packFileService = packFileService;
        private readonly IDbTableQueryService _dbTableQueryService = dbTableQueryService;
        private readonly ILogger _logger = Logging.Create<UnitAudioSwitchResolver>();

        public IReadOnlyDictionary<string, string> Resolve(GameTypeEnum gameType, string variantMeshName, IEnumerable<string> switchGroupNames)
        {
            var requestedSwitchLookups = switchGroupNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(switchGroupName => (
                    SwitchGroupName: switchGroupName,
                    SwitchLookup: GetSwitchLookup(gameType, switchGroupName)))
                .Where(requestedSwitchLookup => requestedSwitchLookup.SwitchLookup != null)
                .ToArray();
            if (string.IsNullOrWhiteSpace(variantMeshName) || requestedSwitchLookups.Length == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var packFileContainers = _packFileService.GetAllPackfileContainers();
            var requestedTableNames = requestedSwitchLookups
                .Select(requestedSwitchLookup => requestedSwitchLookup.SwitchLookup!.ReferencedTableName)
                .Append("variants_tables")
                .Append("unit_variants_tables")
                .Append("land_units_tables")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var tablesByName = _dbTableQueryService.LoadTables(requestedTableNames, packFileContainers);
            var rowsByTableName = tablesByName.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.SelectMany(table => table.Rows).ToArray(),
                StringComparer.OrdinalIgnoreCase);

            var variantRows = rowsByTableName["variants_tables"];
            var unitVariantRows = rowsByTableName["unit_variants_tables"];
            var landUnitRows = rowsByTableName["land_units_tables"];

            var variantNamesForMesh = variantRows
                .Where(variantRow => string.Equals(
                    VariantMeshName.Normalise(variantRow.GetString("variant_filename")),
                    variantMeshName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(variantRow => variantRow.GetString("variant_name"))
                .Where(variantName => string.IsNullOrWhiteSpace(variantName) == false)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var landUnitKeys = unitVariantRows
                .Where(unitVariantRow => variantNamesForMesh.Contains(unitVariantRow.GetString("variant") ?? ""))
                .Select(unitVariantRow => unitVariantRow.GetString("unit"))
                .Where(landUnitKey => string.IsNullOrWhiteSpace(landUnitKey) == false)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var matchingLandUnitRows = landUnitRows
                .Where(landUnitRow => landUnitKeys.Contains(landUnitRow.GetString("key") ?? ""))
                .ToArray();
            if (matchingLandUnitRows.Length == 0)
                _logger.Here().Warning($"Variant mesh '{variantMeshName}' could not be traced to a land unit, so its audio switches fall back to their defaults");

            var switchValuesByGroupName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (switchGroupName, switchLookup) in requestedSwitchLookups)
            {
                var referencedSwitchLookup = switchLookup!;
                var referencedTableKeys = matchingLandUnitRows
                    .Select(landUnitRow => landUnitRow.GetString(referencedSwitchLookup.LandUnitsColumnName))
                    .Where(referencedTableKey => string.IsNullOrWhiteSpace(referencedTableKey) == false)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var switchValues = rowsByTableName[referencedSwitchLookup.ReferencedTableName]
                    .Where(referencedTableRow => referencedTableKeys.Contains(referencedTableRow.GetString("key") ?? ""))
                    .Select(referencedTableRow => referencedTableRow.GetString(referencedSwitchLookup.ReferencedTableSwitchValueColumnName))
                    .Where(switchValue => string.IsNullOrWhiteSpace(switchValue) == false)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(switchValue => switchValue, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (switchValues.Length == 0)
                    continue;

                switchValuesByGroupName[switchGroupName] = switchValues[0];
                if (switchValues.Length > 1)
                    _logger.Here().Warning(
                        $"Variant mesh '{variantMeshName}' resolved multiple values for audio switch group '{switchGroupName}'; " +
                        $"using '{switchValues[0]}'");
            }

            _logger.Here().Information(
                $"Resolved {switchValuesByGroupName.Count} of {requestedSwitchLookups.Length} audio switch groups for variant mesh '{variantMeshName}'");
            return switchValuesByGroupName;
        }

        private static SwitchLookup GetSwitchLookup(GameTypeEnum gameType, string switchGroup)
        {
            return s_switchLookups.FirstOrDefault(lookup => lookup.GameType == gameType
                && string.Equals(lookup.SwitchGroupName, switchGroup, StringComparison.OrdinalIgnoreCase));
        }

    }
}
