using Moq;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.DB;

namespace Shared.CoreTest.Db
{
    internal class DbTableQueryServiceTests
    {
        [Test]
        public void BatchLoadEnumeratesEachContainerOnce()
        {
            var container = new Mock<IPackFileContainer>();
            container.Setup(x => x.GetAllFiles()).Returns([]);
            var service = new DbTableQueryService(Mock.Of<IDbSchemaManager>());

            var result = service.LoadTables(
                ["variants_tables", "unit_variants_tables", "land_units_tables"],
                [container.Object]);

            container.Verify(x => x.GetAllFiles(), Times.Once);
            Assert.That(result.Keys, Is.EquivalentTo(new[]
            {
                "variants_tables",
                "unit_variants_tables",
                "land_units_tables"
            }));
        }
    }
}
