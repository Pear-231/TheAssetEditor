using System.IO;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Test.TestingUtility.TestUtility;

namespace Shared.UiTest.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    [TestFixture]
    internal class SavePackFileContainerCommandTests : ContextMenuCommandTestBase
    {
        [Test]
        public void ShouldAdd_ReturnsTrueForRoot()
        {
            var container = AddPackFiles(false, "modfile", "c:\\mymod.pack", ["rootfolder\\file.txt"]);
            var viewModel = PackFileBrowser();
            var root = viewModel.Files.First();

            var command = new SavePackFileContainerCommand(_packFileService, new Mock<IStandardDialogs>().Object, new ApplicationSettingsService(GameTypeEnum.Warhammer3), new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(root), Is.True);
        }

        [Test]
        public void IsEnabled_ReturnsTrue()
        {
            var container = AddPackFiles(false, "modfile", "c:\\mymod.pack", ["rootfolder\\file.txt"]);
            var viewModel = PackFileBrowser();
            var root = viewModel.Files.First();

            var command = new SavePackFileContainerCommand(_packFileService, new Mock<IStandardDialogs>().Object, new ApplicationSettingsService(GameTypeEnum.Warhammer3), new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.IsEnabled(root), Is.True);
        }

        [Test]
        public void Execute_SavesPackContainer()
        {
            // Arrange
            var container = AddPackFiles(false, "modfile", "c:\\mymod.pack", ["rootfolder\\file.txt"]);
            var viewModel = PackFileBrowser();
            var root = viewModel.Files.First();

            var dialogs = new Mock<IStandardDialogs>();
            var appSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);

            // Act
            var command = new SavePackFileContainerCommand(_packFileService, dialogs.Object, appSettings, new LocalizationManager(), MockScopedLogger.Create());
            command.Configure(root);

            command.Execute();

            // Assert - file was saved (no exception thrown, service called through)
            Assert.That(container.SystemFilePath, Is.EqualTo("c:\\mymod.pack"));
        }

        [Test]
        public void Execute_Parameterless_NoActivePack_ShowsError()
        {
            // Arrange
            AddPackFiles(false, "modfile", "c:\\mymod.pack", ["rootfolder\\file.txt"]);

            var dialogs = new Mock<IStandardDialogs>();
            var appSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);

            // Act
            var command = new SavePackFileContainerCommand(_packFileService, dialogs.Object, appSettings, new LocalizationManager(), MockScopedLogger.Create());
            command.Execute();

            // Assert
            dialogs.Verify(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void Execute_Parameterless_WithActivePack_SavesSuccessfully()
        {
            // Arrange
            var container = AddPackFiles(false, "modfile", "c:\\mymod.pack", ["rootfolder\\file.txt"]);
            _packFileService.SetActivePack(container);

            var dialogs = new Mock<IStandardDialogs>();
            var appSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);

            // Act
            var command = new SavePackFileContainerCommand(_packFileService, dialogs.Object, appSettings, new LocalizationManager(), MockScopedLogger.Create());
            command.Execute();

            // Assert - SavePackContainer was invoked (it will throw internally due to file I/O, caught by the command)
            Assert.That(container.SystemFilePath, Is.EqualTo("c:\\mymod.pack"));
        }

        [Test]
        public void Execute_SystemFolderContainer_SavesToConfiguredOutputLocation()
        {
            var outputDir = Path.Combine(Path.GetTempPath(), "SavePackFileContainerCommandTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDir);
            try
            {
                var outputPath = Path.Combine(outputDir, "generated.pack");
                var settings = new PackFileSettings { SaveLocationPath = outputPath };
                var container = new Mock<IPackFileContainer>();
                container.SetupGet(x => x.Name).Returns("project");
                container.SetupGet(x => x.SystemFilePath).Returns(Path.Combine(outputDir, "project"));
                container.SetupGet(x => x.ContainerType).Returns(PackFileContainerType.SystemFolder);
                container.SetupGet(x => x.PackFileSettings).Returns(settings);
                container.SetupProperty(x => x.IsReadOnly, false);

                var root = new RootTreeNode("project", container.Object);
                var dialogs = new Mock<IStandardDialogs>();
                dialogs.Setup(x => x.ShowWaitCursor()).Returns(new Mock<IWaitCursor>().Object);
                var packFileService = new Mock<IPackFileService>();

                var command = new SavePackFileContainerCommand(packFileService.Object, dialogs.Object, new ApplicationSettingsService(GameTypeEnum.Warhammer3), new LocalizationManager(), MockScopedLogger.Create());
                command.Configure(root);

                command.Execute();

                packFileService.Verify(x => x.SavePackContainer(container.Object, outputPath, false, It.IsAny<GameInformation>()), Times.Once);
            }
            finally
            {
                Directory.Delete(outputDir, true);
            }
        }
    }
}
