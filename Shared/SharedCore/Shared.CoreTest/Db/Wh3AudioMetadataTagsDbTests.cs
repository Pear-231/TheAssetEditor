using System.Globalization;
using Moq;
using Shared.ByteParsing.Parsers;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Db;

namespace Shared.CoreTest.Db
{
    internal class Wh3AudioMetadataTagsDbTests
    {
        private const string TargetTableDirectory = "audio_metadata_tags_tables";
        private const string TargetRowKey = "Foley_Creature_Small_Torso_Slow";

        [Test]
        [Explicit("Local WH3 integration test for schema regeneration/debugging.")]
        public void ReadAudioMetadataTagsRow_FromDataDoubleUnderscore_UsesRegeneratedSchemaAndParsesExpectedValues()
        {
            DirectoryHelper.EnsureCreated();

            var gameDataDirectory = ResolveWarhammer3DataDirectory();
            if (string.IsNullOrWhiteSpace(gameDataDirectory) || !Directory.Exists(gameDataDirectory))
                Assert.Ignore("WH3 data directory was not found. Set ASSETEDITOR_WH3_DATA_DIR or install the game.");

            var schemaPath = Path.Combine(DirectoryHelper.SchemaDirectory, "schema_wh3.json");
            if (File.Exists(schemaPath))
                File.Delete(schemaPath);

            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            settings.CurrentSettings.GameDirectories.Add(new ApplicationSettings.GamePathPair(GameTypeEnum.Warhammer3, gameDataDirectory));

            var schemaManager = new DbSchemaManager(settings);
            schemaManager.EnsureLoaded(GameTypeEnum.Warhammer3);

            Assert.That(File.Exists(schemaPath), Is.True, "Expected schema regeneration to create schema_wh3.json.");

            var waitCursor = new Mock<IWaitCursor>();
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowWaitCursor()).Returns(waitCursor.Object);

            var loader = new PackFileContainerLoader(settings, dialogs.Object, new LocalizationManager());
            var caContainer = loader.LoadAllCaFiles(GameTypeEnum.Warhammer3);
            Assert.That(caContainer, Is.Not.Null, "Unable to load WH3 CA pack files.");

            var queryService = new DbTableQueryService(schemaManager);
            var dataDoubleUnderscoreFile = caContainer!.FindFile("db\\audio_metadata_tags_tables\\data__")
                ?? caContainer.FindFile("db/audio_metadata_tags_tables/data__");

            Assert.That(dataDoubleUnderscoreFile, Is.Not.Null, "Expected to load db/audio_metadata_tags_tables/data__.");

            var selectedTable = queryService.LoadTable(dataDoubleUnderscoreFile!, TargetTableDirectory);
            var limitationScaleColumn = selectedTable.Schema.ColumnSchemas.FirstOrDefault(x => x.Name.Equals("limitation_scale", StringComparison.OrdinalIgnoreCase));
            Assert.That(limitationScaleColumn, Is.Not.Null, "limitation_scale column not found in generated schema.");
            Assert.That(
                limitationScaleColumn!.Type == DbTypesEnum.Single || limitationScaleColumn.Type == DbTypesEnum.Double,
                Is.True,
                "limitation_scale must decode as a numeric floating-point type in WH3 DB tables.");

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
