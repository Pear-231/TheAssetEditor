using Shared.Core.PackFiles.Models;

namespace Shared.CoreTest.PackFiles.Models
{
    [TestFixture]
    public class PackFileContainerTypeTests
    {
        // The recent pack files in the application settings store the container type by numeric value.
        // Changing these values would make existing settings files point at the wrong kind of container.
        [Test]
        public void NumericValues_MatchPersistedSettings()
        {
            Assert.That((int)PackFileContainerType.GamePacks, Is.EqualTo(0));
            Assert.That((int)PackFileContainerType.Pack, Is.EqualTo(1));
            Assert.That((int)PackFileContainerType.Project, Is.EqualTo(2));
        }
    }
}
