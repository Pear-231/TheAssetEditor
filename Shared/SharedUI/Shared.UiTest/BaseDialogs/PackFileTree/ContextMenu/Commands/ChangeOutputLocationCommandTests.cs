using Test.TestingUtility.TestUtility;
using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

namespace Shared.UiTest.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    [TestFixture]
    internal class ChangeOutputLocationCommandTests : ContextMenuCommandTestBase
    {
        [Test]
        public void ShouldAdd_ReturnsTrueForSystemFolderRoot()
        {
            var container = CreateSystemFolderContainer();
            var root = CreateRoot(container.Object);
            var command = new ChangeOutputLocationCommand(new Mock<IStandardDialogs>().Object, new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(root), Is.True);
        }

        [Test]
        public void ShouldAdd_ReturnsFalseForNormalPackRoot()
        {
            var container = CreateSystemFolderContainer(containerType: PackFileContainerType.Pack);
            var root = CreateRoot(container.Object);
            var command = new ChangeOutputLocationCommand(new Mock<IStandardDialogs>().Object, new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(root), Is.False);
        }

        [Test]
        public void Execute_WhenLocationSelected_UpdatesPackFileSettings()
        {
            var settings = new PackFileSettings { SaveLocationPath = @"c:\old\project.pack" };
            var container = CreateSystemFolderContainer(settings: settings);
            var root = CreateRoot(container.Object);
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowSystemSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new SystemSaveFileDialogResult(true, @"c:\new\project_output.pack"));

            var command = new ChangeOutputLocationCommand(dialogs.Object, new LocalizationManager(), MockScopedLogger.Create());
            command.Configure(root);

            command.Execute();

            Assert.That(settings.SaveLocationPath, Is.EqualTo(@"c:\new\project_output.pack"));
            container.Verify(x => x.SaveSettings(), Times.Once);
        }

        [Test]
        public void Execute_WhenCancelled_DoesNotUpdatePackFileSettings()
        {
            var settings = new PackFileSettings { SaveLocationPath = @"c:\old\project.pack" };
            var container = CreateSystemFolderContainer(settings: settings);
            var root = CreateRoot(container.Object);
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowSystemSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new SystemSaveFileDialogResult(false, null));

            var command = new ChangeOutputLocationCommand(dialogs.Object, new LocalizationManager(), MockScopedLogger.Create());
            command.Configure(root);

            command.Execute();

            Assert.That(settings.SaveLocationPath, Is.EqualTo(@"c:\old\project.pack"));
            container.Verify(x => x.SaveSettings(), Times.Never);
        }

        private static Mock<IPackFileContainer> CreateSystemFolderContainer(PackFileContainerType containerType = PackFileContainerType.Project, PackFileSettings? settings = null)
        {
            var container = new Mock<IPackFileContainer>();
            container.SetupGet(x => x.Name).Returns("project");
            container.SetupGet(x => x.SystemFilePath).Returns(@"c:\project");
            container.SetupGet(x => x.ContainerType).Returns(containerType);
            container.SetupGet(x => x.PackFileSettings).Returns(settings ?? new PackFileSettings());
            container.SetupProperty(x => x.IsReadOnly, false);
            return container;
        }
    }
}
