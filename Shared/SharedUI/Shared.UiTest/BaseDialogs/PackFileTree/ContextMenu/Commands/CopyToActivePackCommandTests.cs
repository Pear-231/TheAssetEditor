using Moq;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;
using Test.TestingUtility.TestUtility;

namespace Shared.UiTest.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    [TestFixture]
    internal class CopyToActivePackCommandTests : ContextMenuCommandTestBase
    {
        [Test]
        public void ShouldAdd_ReturnsTrueWhenActivePackExists()
        {
            var source = AddPackFiles(false, "source", "c:\\source.pack", ["folder\\file.txt"]);
            var target = AddPackFiles(false, "target", "c:\\target.pack", []);
            _packFileService.SetActivePack(target);

            var viewModel = PackFileBrowser();
            var node = TreeNodeHelper.FindNode(viewModel, source, "folder\\file.txt");

            var command = new CopyToActivePackCommand(_packFileService, new Mock<IStandardDialogs>().Object, new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(node), Is.True);
        }

        [Test]
        public void ShouldAdd_ReturnsFalseForRootNode()
        {
            var source = AddPackFiles(false, "source", "c:\\source.pack", ["folder\\file.txt"]);
            var target = AddPackFiles(false, "target", "c:\\target.pack", []);
            _packFileService.SetActivePack(target);

            var viewModel = PackFileBrowser();
            var root = viewModel.Files.First(x => (x as RootTreeNode)!.Owner == source);

            var command = new CopyToActivePackCommand(_packFileService, new Mock<IStandardDialogs>().Object, new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(root), Is.False);
        }

        [Test]
        public void IsEnabled_ReturnsTrue()
        {
            var source = AddPackFiles(false, "source", "c:\\source.pack", ["folder\\file.txt"]);
            var viewModel = PackFileBrowser();
            var node = TreeNodeHelper.FindNode(viewModel, source, "folder\\file.txt");

            var command = new CopyToActivePackCommand(_packFileService, new Mock<IStandardDialogs>().Object, new LocalizationManager(), MockScopedLogger.Create());

            Assert.That(command.IsEnabled(node), Is.True);
        }

        [Test]
        public void Execute_CopiesChildFilesToActivePack()
        {
            // Arrange
            var source = AddPackFiles(false, "source", "c:\\source.pack", ["folder\\file.txt"]);
            var target = AddPackFiles(false, "target", "c:\\target.pack", []);
            _packFileService.SetActivePack(target);

            var viewModel = PackFileBrowser();
            var root = viewModel.Files.First(x => (x as RootTreeNode)!.Owner == source);
            var folder = root.Children.First(x => x.NodeType == NodeType.Directory);

            var dialogs = new Mock<IStandardDialogs>();
            var waitCursor = new Mock<IWaitCursor>();
            dialogs.Setup(x => x.ShowWaitCursor()).Returns(waitCursor.Object);

            // Act
            var command = new CopyToActivePackCommand(_packFileService, dialogs.Object, new LocalizationManager(), MockScopedLogger.Create());
            command.Configure(folder);

            command.Execute();

            // Assert
            var copiedFile = target.FindFile("folder\\file.txt");
            Assert.That(copiedFile, Is.Not.Null);
        }
    }
}
