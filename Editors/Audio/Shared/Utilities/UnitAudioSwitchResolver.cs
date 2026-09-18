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
                var referencedTableKeysByLandUnit = matchingLandUnitRows
                    .Select(landUnitRow => (
                        LandUnitKey: landUnitRow.GetString("key"),
                        ReferencedTableKey: landUnitRow.GetString(referencedSwitchLookup.LandUnitsColumnName)))
                    .Where(landUnit => string.IsNullOrWhiteSpace(landUnit.ReferencedTableKey) == false)
                    .ToArray();
                var referencedTableKeys = referencedTableKeysByLandUnit
                    .Select(landUnit => landUnit.ReferencedTableKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // The row each value came from is kept so an ambiguous result can name the
                // land units that disagreed rather than only the value that happened to win.
                var switchValueSources = rowsByTableName[referencedSwitchLookup.ReferencedTableName]
                    .Where(referencedTableRow => referencedTableKeys.Contains(referencedTableRow.GetString("key") ?? ""))
                    .Select(referencedTableRow => (
                        SwitchValue: referencedTableRow.GetString(referencedSwitchLookup.ReferencedTableSwitchValueColumnName),
                        ReferencedTableKey: referencedTableRow.GetString("key")))
                    .Where(switchValueSource => string.IsNullOrWhiteSpace(switchValueSource.SwitchValue) == false)
                    .ToArray();

                var switchValues = switchValueSources
                    .Select(switchValueSource => switchValueSource.SwitchValue)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(switchValue => switchValue, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (switchValues.Length == 0)
                    continue;

                switchValuesByGroupName[switchGroupName] = switchValues[0];
                if (switchValues.Length > 1)
                    _logger.Here().Warning(
                        $"Variant mesh '{variantMeshName}' resolved {switchValues.Length} values for audio switch group '{switchGroupName}': " +
                        $"{DescribeSwitchValues(switchValues, switchValueSources, referencedTableKeysByLandUnit)}. " +
                        $"Using '{switchValues[0]}' because it sorts first, which is arbitrary rather than correct");
            }

            _logger.Here().Information(
                $"Resolved {switchValuesByGroupName.Count} of {requestedSwitchLookups.Length} audio switch groups for variant mesh '{variantMeshName}'");
            return switchValuesByGroupName;
        }

        // Names every candidate alongside the land units that produced it, so an ambiguous
        // switch group can be traced back to the rows that disagreed.
        private static string DescribeSwitchValues(
            IEnumerable<string> switchValues,
            IReadOnlyCollection<(string SwitchValue, string ReferencedTableKey)> switchValueSources,
            IReadOnlyCollection<(string LandUnitKey, string ReferencedTableKey)> referencedTableKeysByLandUnit)
        {
            var descriptions = switchValues.Select(switchValue =>
            {
                var referencedTableKeys = switchValueSources
                    .Where(switchValueSource => string.Equals(switchValueSource.SwitchValue, switchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(switchValueSource => switchValueSource.ReferencedTableKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var landUnitKeys = referencedTableKeysByLandUnit
                    .Where(landUnit => referencedTableKeys.Contains(landUnit.ReferencedTableKey))
                    .Select(landUnit => landUnit.LandUnitKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(landUnitKey => landUnitKey, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return $"'{switchValue}' (from {string.Join(", ", landUnitKeys)})";
            });

            return string.Join("; ", descriptions);
        }

        private static SwitchLookup GetSwitchLookup(GameTypeEnum gameType, string switchGroup)
        {
            return s_switchLookups.FirstOrDefault(lookup => lookup.GameType == gameType
                && string.Equals(lookup.SwitchGroupName, switchGroup, StringComparison.OrdinalIgnoreCase));
        }

    }
}
