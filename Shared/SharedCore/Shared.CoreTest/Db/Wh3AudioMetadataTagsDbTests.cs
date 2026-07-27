using System.Globalization;
using Moq;
using Shared.ByteParsing.Parsers;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Serialization.CacheDatabase;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.DB;
using Test.TestingUtility.TestUtility;

namespace Shared.CoreTest.Db
{
    internal class Wh3AudioMetadataTagsDbTests
    {
        private const string TargetTableDirectory = "audio_metadata_tags_tables";
        private const string TargetRowKey = "Foley_Creature_Small_Torso_Slow";

        [TestCase("audio_metadata_tags_tables", 22, 54)]
        [TestCase("variants_tables", 6, 8)]
        [TestCase("unit_variants_tables", 5, 5)]
        [TestCase("land_units_tables", 54, 60)]
        [TestCase("unit_armour_types_tables", 6, 3)]
        [TestCase("_kv_battle_ai_ability_usage_variables_tables", 0, 2)]
        [Explicit("Remote RPFM schema integration test. Uses the runtime cache when available.")]
        public void EmbeddedSchema_HasRpfmDefinition(string tableName, int version, int expectedColumnCount)
        {
            var schema = new DbSchemaManager().GetSchema(tableName, version);

            Assert.That(schema.ColumnSchemas, Has.Count.EqualTo(expectedColumnCount));
        }

        [Test]
        [Explicit("Remote RPFM schema integration test. Uses the runtime cache when available.")]
        public void EmbeddedSchema_HasRpfmVersion22Definition()
        {
            var schema = new DbSchemaManager().GetSchema(TargetTableDirectory, 22);

            Assert.That(schema.ColumnSchemas, Has.Count.EqualTo(54));
            Assert.That(schema.ColumnSchemas[0].Name, Is.EqualTo("key"));
            Assert.That(schema.ColumnSchemas[0].Type, Is.EqualTo(DbTypesEnum.String));
            Assert.That(schema.ColumnSchemas[1].Name, Is.EqualTo("sound_event_battle_start"));
            Assert.That(schema.ColumnSchemas[1].Type, Is.EqualTo(DbTypesEnum.Optstring));
            Assert.That(schema.ColumnSchemas[^2].Name, Is.EqualTo("persistent_one_shot_duration_override"));
            Assert.That(schema.ColumnSchemas[^2].Type, Is.EqualTo(DbTypesEnum.Double));
            Assert.That(schema.ColumnSchemas[^1].Name, Is.EqualTo("vocalisation_bank_split"));
        }

        [Test]
        [Explicit("Local WH3 integration test. Set ASSETEDITOR_WH3_DATA_DIR when the game is installed elsewhere.")]
        public void ReadAudioMetadataTagsRow_FromDataDoubleUnderscore_UsesEmbeddedRpfmSchema()
        {
            DirectoryHelper.EnsureCreated();

            var gameDataDirectory = ResolveWarhammer3DataDirectory();
            if (string.IsNullOrWhiteSpace(gameDataDirectory) || !Directory.Exists(gameDataDirectory))
                Assert.Ignore("WH3 data directory was not found. Set ASSETEDITOR_WH3_DATA_DIR or install the game.");

            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            settings.CurrentSettings.GameDirectories.Add(new ApplicationSettings.GamePathPair(GameTypeEnum.Warhammer3, gameDataDirectory));

            var waitCursor = new Mock<IWaitCursor>();
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowWaitCursor()).Returns(waitCursor.Object);

            var loader = new PackFileContainerLoader(
                settings,
                dialogs.Object,
                new LocalizationManager(),
                new PackFileContainerCacheHelper(),
                new SimpleSystemFolderContainerFactory());
            var caContainer = loader.CreateFromGameEnum(PackFileContainerType.Database, GameTypeEnum.Warhammer3);
            Assert.That(caContainer, Is.Not.Null, "Unable to load WH3 CA pack files.");

            var queryService = new DbTableQueryService(new DbSchemaManager());
            var dataDoubleUnderscoreFile = caContainer!.FindFile("db\\audio_metadata_tags_tables\\data__")
                ?? caContainer.FindFile("db/audio_metadata_tags_tables/data__");

            Assert.That(dataDoubleUnderscoreFile, Is.Not.Null, "Expected to load db/audio_metadata_tags_tables/data__.");

            var selectedTable = queryService.LoadTable(dataDoubleUnderscoreFile!, TargetTableDirectory);
            var limitationScaleColumn = selectedTable.Schema.ColumnSchemas.Single(x => x.Name == "limitation_scale");
            Assert.That(limitationScaleColumn.Type, Is.EqualTo(DbTypesEnum.Single));

            var targetRow = selectedTable.Rows.FirstOrDefault(x => string.Equals(x.GetString("key"), TargetRowKey, StringComparison.Ordinal));
            Assert.That(targetRow, Is.Not.Null, $"Expected row key '{TargetRowKey}' was not found.");

            var row = targetRow!;
            Assert.That(row.GetString("sound_event_battle_start"), Is.EqualTo("Battle_Individual_Foley_Creature_Small_Torso_Slow"));
            Assert.That(row.GetString("sound_event_campaign_start"), Is.EqualTo("Campaign_Individual_Foley_Creature_Small_Torso_Slow"));
            Assert.That(GetSingle(row, "limitation_scale"), Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(GetSingle(row, "culling_distance_override"), Is.EqualTo(30f).Within(0.0001f));
            Assert.That(GetBool(row, "play_at_base"), Is.True);
            Assert.That(GetBool(row, "require_armour_type"), Is.True);
            Assert.That(GetBool(row, "require_shield_type"), Is.False);
            Assert.That(GetBool(row, "is_tracked"), Is.True);
            Assert.That(GetBool(row, "require_reverb"), Is.True);
            Assert.That(GetBool(row, "require_obstruction"), Is.False);
            Assert.That(GetBool(row, "can_play_under_splice"), Is.True);

            var containers = new List<IPackFileContainer> { caContainer };
            var variantRow = queryService.LoadTables("variants_tables", containers)
                .SelectMany(x => x.Rows)
                .Single(x => x.GetString("variant_name") == "wh2_dlc16_skv_throt");
            Assert.That(variantRow.GetString("variant_filename"), Is.EqualTo("skv_throt"));

            var unitVariantRow = queryService.LoadTables("unit_variants_tables", containers)
                .SelectMany(x => x.Rows)
                .First(x => x.GetString("variant") == "wh2_dlc16_skv_throt");
            Assert.That(unitVariantRow.GetString("unit"), Is.EqualTo("wh2_dlc16_skv_cha_throt_the_unclean_0"));

            var landUnitRow = queryService.LoadTables("land_units_tables", containers)
                .SelectMany(x => x.Rows)
                .Single(x => x.GetString("key") == "wh2_dlc16_skv_cha_throt_the_unclean_0");
            Assert.That(landUnitRow.GetString("armour"), Is.EqualTo("wh2_main_body_45"));

            var armourTypeRow = queryService.LoadTables("unit_armour_types_tables", containers)
                .SelectMany(x => x.Rows)
                .Single(x => x.GetString("key") == "wh2_main_body_45");
            Assert.That(armourTypeRow.GetString("audio_type"), Is.EqualTo("body"));
        }

        [Test]
        public void RpfmRonSchema_CanResolveTableByNameAndVersion()
        {
            const string ron = """
                (
                    version: 5,
                    definitions: {
                        "example_tables": [
                            (
                                version: 7,
                                fields: [
                                    (
                                        name: "key",
                                        field_type: StringU8,
                                        is_key: true,
                                        default_value: None,
                                        is_filename: false,
                                        filename_relative_path: None,
                                        is_reference: Some(("other_tables", "key")),
                                        lookup: None,
                                        description: "",
                                        ca_order: 0,
                                        is_bitwise: 0,
                                        enum_values: {},
                                        is_part_of_colour: None,
                                    ),
                                ],
                                localised_fields: [],
                                localised_key_order: [],
                            ),
                        ],
                    },
                )
                """;

            var schema = DbSchemaManager.CreateFromRon(ron).GetSchema("example_tables", 7);

            Assert.That(schema.ColumnSchemas, Has.Count.EqualTo(1));
            Assert.That(schema.ColumnSchemas[0].Name, Is.EqualTo("key"));
            Assert.That(schema.ColumnSchemas[0].IsKey, Is.True);
            Assert.That(schema.ColumnSchemas[0].TableReference, Is.EqualTo("other_tables"));
            Assert.That(schema.ColumnSchemas[0].FieldReference, Is.EqualTo("key"));
        }

        private static bool GetBool(DbTableRow row, string columnName)
        {
            var value = row.GetValue(columnName);
            Assert.That(value, Is.Not.Null, $"Column '{columnName}' was null.");
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        private static float GetSingle(DbTableRow row, string columnName)
        {
            var value = row.GetValue(columnName);
            Assert.That(value, Is.Not.Null, $"Column '{columnName}' was null.");
            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }

        private static string? ResolveWarhammer3DataDirectory()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("ASSETEDITOR_WH3_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                return fromEnvironment;

            var rootCandidates = new[]
            {
                @"D:\SteamLibrary\steamapps\common\Total War WARHAMMER III",
                @"C:\Program Files (x86)\Steam\steamapps\common\Total War WARHAMMER III",
                @"C:\Program Files\Steam\steamapps\common\Total War WARHAMMER III"
            };

            foreach (var rootCandidate in rootCandidates)
            {
                var dataPath = Path.Combine(rootCandidate, "data");
                if (Directory.Exists(dataPath))
                    return dataPath;
            }

            return null;
        }
    }
}
