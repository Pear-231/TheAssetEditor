using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Test.TestingUtility.TestUtility;

namespace Shared.UiTest.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    [TestFixture]
    internal class ToggleIgnoredForSerializationCommandTests : ContextMenuCommandTestBase
    {
        [Test]
        public void ShouldAdd_ReturnsTrue_ForSystemFolderFileNode()
        {
            var settings = new PackFileSettings();
            var container = CreateContainer(PackFileContainerType.Project, settings);
            var root = CreateRoot(container.Object);
            var fileNode = CreateNodePath(root, "folder\\file.txt", NodeType.File);

            var command = new ToggleIgnoredForSerializationCommand(new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(fileNode), Is.True);
        }

        [Test]
        public void ShouldAdd_ReturnsFalse_ForDirectoryNode()
        {
            var settings = new PackFileSettings();
            var container = CreateContainer(PackFileContainerType.Project, settings);
            var root = CreateRoot(container.Object);
            var dirNode = CreateNodePath(root, "folder", NodeType.Directory);

            var command = new ToggleIgnoredForSerializationCommand(new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(dirNode), Is.False);
        }

        [Test]
        public void ShouldAdd_ReturnsFalse_ForNormalPackFileNode()
        {
            var settings = new PackFileSettings();
            var container = CreateContainer(PackFileContainerType.Pack, settings);
            var root = CreateRoot(container.Object);
            var fileNode = CreateNodePath(root, "folder\\file.txt", NodeType.File);

            var command = new ToggleIgnoredForSerializationCommand(new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(fileNode), Is.False);
        }

        [Test]
        public void GetDisplayName_ReturnsAdd_WhenFileIsNotIgnored()
        {
            var settings = new PackFileSettings();
            var container = CreateContainer(PackFileContainerType.Project, settings);
            var root = CreateRoot(container.Object);
            var fileNode = CreateNodePath(root, "folder\\file.txt", NodeType.File);

            var command = new ToggleIgnoredForSerializationCommand(new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.GetDisplayName(fileNode), Is.EqualTo("PackFileTree.ContextMenu.AddToIgnoredFiles"));
        }

        [Test]
        public void GetDisplayName_ReturnsRemove_WhenFileIsIgnored()
        {
            var settings = new PackFileSettings();
            settings.IgnoredFilesWhenSerializing.Add("folder\\file.txt");
            var container = CreateContainer(PackFileContainerType.Project, settings);
            var root = CreateRoot(container.Object);
            var fileNode = CreateNodePath(root, "folder\\file.txt", NodeType.File);

            var command = new ToggleIgnoredForSerializationCommand(new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.GetDisplayName(fileNode), Is.EqualTo("PackFileTree.ContextMenu.RemoveFromIgnoredFiles"));
        }

        [Test]
        public void GetDisplayName_ReturnsRemove_WhenIgnoredEntryUsesForwardSlash()
        {
            var settings = new PackFileSettings();
            settings.IgnoredFilesWhenSerializing.Add("folder/file.txt");
            var container = CreateContainer(PackFileContainerType.Project, settings);
            var root = CreateRoot(container.Object);
            var fileNode = CreateNodePath(root, "folder\\file.txt", NodeType.File);

            var command = new ToggleIgnoredForSerializationCommand(new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.GetDisplayName(fileNode), Is.EqualTo("PackFileTree.ContextMenu.RemoveFromIgnoredFiles"));
        }

        [Test]
        public void Execute_TogglesIgnoredState_AddThenRemove()
        {
            var settings = new PackFileSettings();
            var container = CreateContainer(PackFileContainerType.Project, settings);
            var root = CreateRoot(container.Object);
            var fileNode = CreateNodePath(root, "folder\\file.txt", NodeType.File);

            var command = new ToggleIgnoredForSerializationCommand(new LocalizationManager(), MockScopedLogger.Create());
            command.Configure(fileNode);

            command.Execute();
            Assert.That(settings.IgnoredFilesWhenSerializing.Contains("folder\\file.txt", StringComparer.OrdinalIgnoreCase), Is.True);

            command.Execute();
            Assert.That(settings.IgnoredFilesWhenSerializing.Contains("folder\\file.txt", StringComparer.OrdinalIgnoreCase), Is.False);
            container.Verify(x => x.SaveSettings(), Times.Exactly(2));
        }

        [Test]
        public void Execute_RaisesVisualRefreshNotification_ForNodeBindingReevaluation()
        {
            var settings = new PackFileSettings();
            var container = CreateContainer(PackFileContainerType.Project, settings);
            var root = CreateRoot(container.Object);
            var fileNode = CreateNodePath(root, "folder\\file.txt", NodeType.File);

            var changedProperties = new List<string?>();
            fileNode.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

            var command = new ToggleIgnoredForSerializationCommand(new LocalizationManager(), MockScopedLogger.Create());
            command.Configure(fileNode);

            command.Execute();

            Assert.That(changedProperties, Does.Contain("IconNode"));
        }

        private static Mock<IPackFileContainer> CreateContainer(PackFileContainerType containerType, PackFileSettings settings)
        {
            var container = new Mock<IPackFileContainer>();
            container.SetupGet(x => x.Name).Returns("container");
            container.SetupGet(x => x.SystemFilePath).Returns("C:\\temp\\project");
            container.SetupGet(x => x.ContainerType).Returns(containerType);
            container.SetupGet(x => x.PackFileSettings).Returns(settings);
            return container;
        }
    }
}
