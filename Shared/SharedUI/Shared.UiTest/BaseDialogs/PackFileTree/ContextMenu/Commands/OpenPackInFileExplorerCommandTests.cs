using Test.TestingUtility.TestUtility;
using System.Diagnostics;
using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

namespace Shared.UiTest.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    [TestFixture]
    internal class OpenPackInFileExplorerCommandTests : ContextMenuCommandTestBase
    {
        [Test]
        public void ShouldAdd_ReturnsTrueForRoot()
        {
            AddPackFiles(false, "modfile", "c:\\mymod.pack", ["rootfolder\\file.txt"]);
            var viewModel = PackFileBrowser();
            var root = viewModel.Files.First();

            var command = new OpenPackInFileExplorerCommand(_packFileService, new Mock<IStandardDialogs>().Object, new Mock<IFileSystemAccess>().Object, new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(root), Is.True);
        }

        [Test]
        public void ShouldAdd_ReturnsTrueForSystemFolderFileNode()
        {
            var container = CreateContainer(PackFileContainerType.SystemFolder);
            var root = CreateRoot(container);
            var fileNode = CreateNodePath(root, "folder\\file.txt", NodeType.File);

            var command = new OpenPackInFileExplorerCommand(_packFileService, new Mock<IStandardDialogs>().Object, new Mock<IFileSystemAccess>().Object, new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(fileNode), Is.True);
        }

        [Test]
        public void ShouldAdd_ReturnsFalseForNonSystemFolderFileNode()
        {
            var container = CreateContainer(PackFileContainerType.Normal);
            var root = CreateRoot(container);
            var fileNode = CreateNodePath(root, "folder\\file.txt", NodeType.File);

            var command = new OpenPackInFileExplorerCommand(_packFileService, new Mock<IStandardDialogs>().Object, new Mock<IFileSystemAccess>().Object, new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(fileNode), Is.False);
        }

        [Test]
        public void IsEnabled_ReturnsTrue()
        {
            AddPackFiles(false, "modfile", "c:\\mymod.pack", ["rootfolder\\file.txt"]);
            var viewModel = PackFileBrowser();
            var root = viewModel.Files.First();

            var command = new OpenPackInFileExplorerCommand(_packFileService, new Mock<IStandardDialogs>().Object, new Mock<IFileSystemAccess>().Object, new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.IsEnabled(root), Is.True);
        }

        [Test]
        public void Execute_ValidPath_StartsExplorer()
        {
            // Arrange
            AddPackFiles(false, "modfile", "c:\\temp\\pack.pack", ["rootfolder\\file.txt"]);
            var viewModel = PackFileBrowser();
            var root = viewModel.Files.First();

            var fileSystem = new Mock<IFileSystemAccess>();
            fileSystem.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(false);
            fileSystem.Setup(x => x.PathGetDirectoryName(It.IsAny<string>())).Returns("c:\\temp");

            // Act
            var command = new OpenPackInFileExplorerCommand(_packFileService, new Mock<IStandardDialogs>().Object, fileSystem.Object, new LocalizationManager(), MockScopedLogger.Create());
            command.Configure(root);

            command.Execute();

            // Assert
            fileSystem.Verify(x => x.ProcessStart(It.Is<ProcessStartInfo>(p => p.FileName == "explorer.exe")), Times.Once);
        }

        [Test]
        public void Execute_NullSystemFilePath_ShowsError()
        {
            // Arrange
            AddPackFiles(false, "modfile", "", ["rootfolder\\file.txt"]);
            var viewModel = PackFileBrowser();
            var root = viewModel.Files.First();

            var dialogs = new Mock<IStandardDialogs>();

            // Act
            var command = new OpenPackInFileExplorerCommand(_packFileService, dialogs.Object, new Mock<IFileSystemAccess>().Object, new LocalizationManager(), MockScopedLogger.Create());
            command.Configure(root);

            command.Execute();

            // Assert
            dialogs.Verify(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        private static IPackFileContainer CreateContainer(PackFileContainerType containerType)
        {
            var container = new Mock<IPackFileContainer>();
            container.SetupGet(x => x.Name).Returns("container");
            container.SetupGet(x => x.SystemFilePath).Returns("C:\\temp\\project");
            container.SetupGet(x => x.ContainerType).Returns(containerType);
            container.SetupGet(x => x.IsReadOnly).Returns(false);
            return container.Object;
        }
    }
}
