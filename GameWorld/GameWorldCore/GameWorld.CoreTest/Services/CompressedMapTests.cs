using Shared.GameFormats.CompressedMap;

namespace GameWorld.Core.Test.Services
{
    internal class CompressedMapTests
    {
        [Test]
        public void WakaHeightMap_DecodesHeaderAndKnownTerryRegions()
        {
            var path = FindRepositoryFile(
                "Research", "maps", "example map pack", "terrain", "tiles", "battle",
                "domination", "waka_chateau", "tile_height_map.compressed_map");

            var map = CompressedMapParser.Parse(File.ReadAllBytes(path));

            Assert.That(map.Version, Is.EqualTo(3));
            Assert.That(map.Width, Is.EqualTo(1280));
            Assert.That(map.Height, Is.EqualTo(1280));
            Assert.That(map.BlockWidth, Is.EqualTo(16));
            Assert.That(map.BlockHeight, Is.EqualTo(16));
            Assert.That(map.CodecName, Is.EqualTo("TABLE_INDEXED"));
            Assert.That(map.ValueMinimum, Is.EqualTo(0f));
            Assert.That(map.ValueMaximum, Is.EqualTo(56.92617f).Within(0.00001f));

            var heights = map.ToFloatSamples();
            Assert.That(heights[0, 0], Is.EqualTo(0f));
            Assert.That(heights[304, 256], Is.EqualTo(1.6696f).Within(0.001f));
            Assert.That(heights[576, 304], Is.EqualTo(10.14728f).Within(0.001f));
            Assert.That(heights[528, 320], Is.EqualTo(10.06935f).Within(0.001f));
        }

        [Test]
        public void WakaHeightMap_DecodesAllObservedBlockModesWithinUInt16Range()
        {
            var path = FindRepositoryFile(
                "Research", "maps", "example map pack", "terrain", "tiles", "battle",
                "domination", "waka_chateau", "tile_height_map.compressed_map");

            var map = CompressedMapParser.Parse(File.ReadAllBytes(path));

            Assert.That(map.Samples.Cast<ushort>().Min(), Is.EqualTo(0));
            Assert.That(map.Samples.Cast<ushort>().Max(), Is.GreaterThan(60000));
        }

        [Test]
        public void WakaCompanionMaps_DecodePartialAndConstantBlockGrids()
        {
            var alphaPath = FindRepositoryFile(
                "Research", "maps", "example map pack", "terrain", "tiles", "battle",
                "domination", "waka_chateau", "tile_alpha_map.compressed_map");
            var deltaPath = FindRepositoryFile(
                "Research", "maps", "example map pack", "terrain", "tiles", "battle",
                "domination", "waka_chateau", "tile_height_map_logic_delta.compressed_map");
            var logicPath = FindRepositoryFile(
                "Research", "maps", "example map pack", "terrain", "battles",
                "test_domination_waka_chateau", "full_lf_logic_map.compressed_map");

            var alpha = CompressedMapParser.Parse(File.ReadAllBytes(alphaPath));
            var delta = CompressedMapParser.Parse(File.ReadAllBytes(deltaPath));
            var logic = CompressedMapParser.Parse(File.ReadAllBytes(logicPath));

            Assert.That((alpha.Width, alpha.Height), Is.EqualTo((1281, 1281)));
            Assert.That((delta.Width, delta.Height), Is.EqualTo((1280, 1280)));
            Assert.That((logic.Width, logic.Height), Is.EqualTo((112, 112)));
            Assert.That(delta.Samples.Cast<ushort>().All(x => x == 0), Is.True);
            Assert.That(logic.Samples.Cast<ushort>().All(x => x == 0), Is.True);
        }

        private static string FindRepositoryFile(params string[] relativeParts)
        {
            for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                var candidate = Path.Combine([directory.FullName, .. relativeParts]);
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(relativeParts)}'.");
        }
    }
}
