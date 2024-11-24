﻿using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace Shared.TestUtility
{
    public static class PackFileSerivceTestHelper
    {
        public static PackFileService Create(string path, GameTypeEnum gameTypeEnum = GameTypeEnum.Warhammer3)
        {
            var pfs = new PackFileService(null);
            var loader = new PackFileContainerLoader(new ApplicationSettingsService(gameTypeEnum), new GameInformationFactory());
            var container = loader.LoadSystemFolderAsPackFileContainer(path);
            container.IsCaPackFile = true;
            pfs.AddContainer(container);
            
            return pfs;
        }

        public static PackFileService CreateFromFolder(GameTypeEnum selectedGame, string path )
        {
            var pfs = new PackFileService(null);
            var loader = new PackFileContainerLoader(new ApplicationSettingsService(selectedGame), new GameInformationFactory());

            var container = loader.LoadSystemFolderAsPackFileContainer(PathHelper.Folder(path));
            container.IsCaPackFile = true;
            pfs.AddContainer(container);
            return pfs;
        }
    }
}