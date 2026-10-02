using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree;

namespace Shared.UiTest.BaseDialogs.PackFileTree
{
    // An unloaded LocalizationManager returns the loc key, so the tests assert on the keys.
    [TestFixture]
    public class RootTreeNodeFlagsTests
    {
        private static RootTreeNode CreateRoot(PackFileContainerType type, bool isReadOnly)
        {
            var container = new Mock<IPackFileContainer>();
            container.SetupGet(x => x.ContainerType).Returns(type);
            container.SetupGet(x => x.IsReadOnly).Returns(isReadOnly);
            return new RootTreeNode("test", container.Object, new LocalizationManager());
        }

        [Test]
        public void Flags_GamePacks_AreGamePacksAndReadOnly()
        {
            var root = CreateRoot(PackFileContainerType.GamePacks, true);

            Assert.That(root.Flags, Is.EqualTo("PackFile.Flag.GamePacks, PackFile.Flag.ReadOnly"));
        }

        [Test]
        public void Flags_ReadOnlyPack_AreKindThenReadOnly()
        {
            var root = CreateRoot(PackFileContainerType.Pack, true);

            Assert.That(root.Flags, Is.EqualTo("PackFile.Flag.Pack, PackFile.Flag.ReadOnly"));
        }

        [Test]
        public void Flags_WritablePack_IsKindOnly()
        {
            var root = CreateRoot(PackFileContainerType.Pack, false);

            Assert.That(root.Flags, Is.EqualTo("PackFile.Flag.Pack"));
        }

        [Test]
        public void Flags_ActiveProject_AreKindThenActive()
        {
            var root = CreateRoot(PackFileContainerType.Project, false);
            root.IsActivePack = true;

            Assert.That(root.Flags, Is.EqualTo("PackFile.Flag.Project, PackFile.Flag.Active"));
        }

        [Test]
        public void Flags_IsActivePackChanges_RaisesPropertyChangedAndUpdatesFlags()
        {
            var root = CreateRoot(PackFileContainerType.Project, false);
            var changed = new List<string?>();
            root.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            root.IsActivePack = true;
            Assert.That(changed, Does.Contain(nameof(RootTreeNode.Flags)));
            Assert.That(root.Flags, Does.Contain("PackFile.Flag.Active"));

            root.IsActivePack = false;
            Assert.That(root.Flags, Is.EqualTo("PackFile.Flag.Project"));
        }
    }
}
