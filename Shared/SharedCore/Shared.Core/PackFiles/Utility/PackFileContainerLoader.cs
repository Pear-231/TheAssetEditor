using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Shared.Core.PackFiles.ErrorHandling;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Serialization.CacheDatabase;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace Shared.Core.PackFiles.Utility
{
    public interface ISystemFolderContainerFactory
    {
        IPackFileContainer Create(string folderPath);
    }

    public class SystemFolderContainerFactory : ISystemFolderContainerFactory
    {
        private readonly IFileSystemAccess _fileSystemAccess;
        private readonly Func<IFileSystemWatcher> _watcherFactory;

        public SystemFolderContainerFactory(IFileSystemAccess fileSystemAccess, Func<IFileSystemWatcher> watcherFactory)
        {
            _fileSystemAccess = fileSystemAccess;
            _watcherFactory = watcherFactory;
        }

        public IPackFileContainer Create(string folderPath)
        {
            return new SystemFolderContainer(folderPath, _fileSystemAccess, _watcherFactory());
        }
    }


    public interface IPackFileContainerLoader
    {
        IPackFileContainer CreateFromPackFile(PackFileContainerType type, string packFilePath, bool loadAsReadOnly);
        IPackFileContainer? CreateFromGameEnum(PackFileContainerType type, GameTypeEnum game);
        IPackFileContainer CreateFromSystemFolder(string folderPath);
    }

    class PackFileContainerLoader : IPackFileContainerLoader
    {
        static private readonly ILogger _logger = Logging.CreateStatic(typeof(PackFileContainerLoader));
        private readonly ApplicationSettingsService _settingsService;
        private readonly IStandardDialogs _standardDialogs;
        private readonly LocalizationManager _localizationManager;
        private readonly IPackFileContainerCacheHelper _packFileContainerCacheHelper;
        private readonly ISystemFolderContainerFactory _systemFolderContainerFactory;

        public PackFileContainerLoader(
            ApplicationSettingsService settingsService,
            IStandardDialogs standardDialogs,
            LocalizationManager localizationManager,
            IPackFileContainerCacheHelper packFileContainerCacheHelper,
            ISystemFolderContainerFactory systemFolderContainerFactory)
        {
            _settingsService = settingsService;
            _standardDialogs = standardDialogs;
            _localizationManager = localizationManager;
            _packFileContainerCacheHelper = packFileContainerCacheHelper;
            _systemFolderContainerFactory = systemFolderContainerFactory;
        }

        public IPackFileContainer CreateFromSystemFolder(string packFileSystemPath)
        {
            if (Directory.Exists(packFileSystemPath) == false)
            {
                var location = Assembly.GetEntryAssembly()!.Location;
                var loactionDir = Path.GetDirectoryName(location);
                throw new Exception($"Unable to find folder {packFileSystemPath}. Curret systempath is {loactionDir}");
            }

            var container = _systemFolderContainerFactory.Create(packFileSystemPath);
            if (container.PackFileSettings.GameVersion == GameTypeEnum.Unknown)
            {
                container.PackFileSettings.GameVersion = _settingsService.CurrentSettings.CurrentGame;
                container.SaveSettings();
            }
            StampGameType(container, container.PackFileSettings.GameVersion.Value);
            return container;
        }

        public IPackFileContainer CreateFromPackFile(PackFileContainerType type, string packFilePath, bool loadAsReadOnly)
        {
            var packfileName = Path.GetFileNameWithoutExtension(packFilePath);
            var game = _settingsService.CurrentSettings.CurrentGame;
            var container = CreateFromCollection(type, packFilePath, [packFilePath], packfileName, loadAsReadOnly, new CustomPackDuplicateFileResolver(), game);
            
            // For all supported games, packs with the HasEncryptedData flag set contain only encrypted files
            if (container is PackFileContainer { Header.HasEncryptedData: true } packContainer)
                ValidateGameCompatibility(packContainer, game, packFilePath);
            
            container.PackFileSettings.GameVersion = game;
            container.SaveSettings();
            StampGameType(container, container.PackFileSettings.GameVersion.Value);
            return container;
        }

        private void ValidateGameCompatibility(PackFileContainer packContainer, GameTypeEnum game, string packFilePath)
        {
            var gameInformation = GameInformationDatabase.GetGameById(game);
            var appearsCompatible = true;

            if (packContainer.Header.Version != gameInformation.PackFileVersion)
                appearsCompatible = false;
            else
            {
                // Because the keystream cannot be derived from the pack and has to be figured out manually
                // encrypted packs may not be loaded correctly if the loaded pack does not match the active game.
                // This may work fine coincidentally if both the active game and the game the pack is intended for
                // share the same encryption keystream, however if they don't e.g. the active game is Attila
                // (32-bit keystream) but the pack is intended for Warhammer III (64-bit keystream), then the
                // encrypted files will not decrypt correctly.
                appearsCompatible = HasValidEncryptedContent(packContainer);
            }

            if (appearsCompatible)
                return;

            _standardDialogs.ShowDialogBox(
                $"{Path.GetFileName(packFilePath)} does not appear to be for the active game in the settings " +
                $"({gameInformation.DisplayName}). Set the active game to the game the pack file is intended for.");
        }

        private static bool HasValidEncryptedContent(IPackFileContainer container)
        {
            foreach (var (path, file) in container.GetAllFiles())
            {
                if (file.DataSource is not PackedFileSource { IsEncrypted: true } source)
                    continue;

                if (source.IsCompressed && source.CompressionFormat == CompressionFormat.None)
                    return false;

                try
                {
                    var bytes = source.ReadData();
                    if (FileEncryption.TryValidateDecryptedContent(path, bytes, out var error) && error != null)
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        public IPackFileContainer? CreateFromGameEnum(PackFileContainerType type, GameTypeEnum gameEnum)
        {
            var game = GameInformationDatabase.GetGameById(gameEnum);
            var gamePathInfo = _settingsService.CurrentSettings.GameDirectories.FirstOrDefault(x => x.Game == game.Type);
            var gameName = game.DisplayName;

            if (gamePathInfo == null || string.IsNullOrWhiteSpace(gamePathInfo.Path))
            {
                var errorMessage = $"Unable to load pack files for {gameName} because no game directory is configured.";
                _logger.Here().Error(errorMessage);
                return null;
            }

            var gameDataFolder = gamePathInfo.Path;
            var fullPackFilePaths = ManifestHelper.GetPackFilesFromManifest(gameDataFolder, out var manifestFileFound);

            // When loading ca pack packs, we want to use the CA resolver as its faster.
            // If there is no manifest file, we need to use the duplicate resolver as it loads all file in the folder.
            // There might be custom mods in there that does not follow the rules!
            IDuplicateFileResolver packfileResolver = new CaPackDuplicateFileResolver();
            if (manifestFileFound == false)
            {
                _logger.Here().Warning($"Loading pack files for {gameName}, which does not uses manifest.txt. If there are MODs in the game folder, this might cause issues!");
                packfileResolver = new CustomPackDuplicateFileResolver();
            }

            var container = CreateFromCollection(PackFileContainerType.Database, gameDataFolder, fullPackFilePaths, $"All Game Packs - {gameName}", true, packfileResolver, gameEnum);
            container.IsCaPackFile = true;
            container.PackFileSettings.GameVersion = gameEnum;
            container.SaveSettings();
            StampGameType(container, gameEnum);
            return container;
        }

        public IPackFileContainer CreateFromCollection(PackFileContainerType type, string packFileSystemPath, List<string> fullPackFilePaths, string createdPackFileName, bool loadAsReadOnly, IDuplicateFileResolver duplicateFileResolver, GameTypeEnum game)
        {
            if (type == PackFileContainerType.Database && loadAsReadOnly == false)
                throw new InvalidOperationException($"Cannot load as writable if loading from cache. Caching is only supported for read-only containers. PackFile {createdPackFileName}");

            var fingerprint = string.Empty;
            var cacheFilePath = string.Empty;
            if (type == PackFileContainerType.Database)
            {
                fingerprint = _packFileContainerCacheHelper.ComputeFingerprint(fullPackFilePaths);
                var cachePrefix = createdPackFileName;
                cacheFilePath = _packFileContainerCacheHelper.GetCacheFilePath(cachePrefix, fingerprint);

                var cached = _packFileContainerCacheHelper.TryLoadFromCache(cacheFilePath, fingerprint);
                if (cached != null && cached.PackFileSettings.GameVersion == game)
                    return cached;

                //var cacheInvalidReason = GetCacheInvalidReason(cacheFilePath, fingerprint);
                //_logger.Here().Information($"Cache invalid reason for {gameName}: {cacheInvalidReason}");
                //var reasonMessage = string.Format(_localizationManager.Get("PackFileCache.InvalidReason." + cacheInvalidReason), gameName);
                //var buildingMessage = string.Format(_localizationManager.Get("PackFileCache.BuildingCache"), gameName);
                //var cacheDescription = _localizationManager.Get("PackFileCache.Description");
                _standardDialogs.ShowDialogBox("Failed to load from cache - Generating new cache");
            }

            using (_standardDialogs.ShowWaitCursor())
            {
                var container = LoadPackFilesFromDisk(createdPackFileName, fullPackFilePaths, duplicateFileResolver, game);
                container.Name = createdPackFileName;
                container.IsReadOnly = loadAsReadOnly;
                container.SystemFilePath = packFileSystemPath;
                container.PackFileSettings.GameVersion = game;

                if (type == PackFileContainerType.Database)
                {
                    return _packFileContainerCacheHelper.SaveAndLoadCache(fingerprint, container, cacheFilePath);
                }

                return container;
            }
        }

        private static PackFileContainer LoadPackFilesFromDisk(string createdPackFileName, List<string> fullPackFilePaths, IDuplicateFileResolver packfileResolver, GameTypeEnum game)
        {
            var packList = new ConcurrentBag<PackFileContainer>();
            var packsCompressionStats = new ConcurrentDictionary<CompressionFormat, CompressionInformation>();

            Parallel.ForEach(fullPackFilePaths, packFilePath =>
            {
                var path = packFilePath;
                if (File.Exists(path))
                {
                    using var fileStream = File.OpenRead(path);
                    using var reader = new BinaryReader(fileStream, Encoding.ASCII);

                    var packFileSize = new FileInfo(path).Length;
                    var pack = PackFileSerializerLoader.Load(path, packFileSize, reader, packfileResolver, game);
                    packList.Add(pack);

                    PackFileLog.LogPackCompression(pack);
                    var packCompressionStats = PackFileLog.GetCompressionInformation(pack);
                    foreach (var kvp in packCompressionStats)
                    {
                        packsCompressionStats.AddOrUpdate(
                            kvp.Key,
                            _ => new CompressionInformation(kvp.Value.DiskSize, kvp.Value.UncompressedSize),
                            (_, existingStats) => new CompressionInformation(
                                existingStats.DiskSize + kvp.Value.DiskSize,
                                existingStats.UncompressedSize + kvp.Value.UncompressedSize));
                    }
                }
                else
                    _logger.Here().Warning($"{createdPackFileName} pack file '{path}' not found, loading skipped");
            }
            );

            PackFileLog.LogPacksCompression(packsCompressionStats);

            // If there is only one packfile - we dont need to sort. Just return it.
            // Be aware, that when we create a new PackFileContainer in the case of multiple packfiles, we will lose the original header information of the first packfile.
            // This is because we need to create a new header for the new container. This should not be an issue, but its something to be aware of.
            if (packList.Count == 1)
                return packList.First();

            var mergedPackFile = PackFileContainer.CreatePackFile(createdPackFileName);
            var packFilesOrderedByGroup = packList.GroupBy(x => x.Header.LoadOrder).OrderBy(x => x.Key);

            foreach (var group in packFilesOrderedByGroup)
            {
                var packFilesOrderedByName = group.OrderBy(x => x.Name);
                foreach (var packfile in packFilesOrderedByName)
                {
                    if (string.IsNullOrWhiteSpace(packfile.SystemFilePath) == false)
                        mergedPackFile.SourcePackFilePaths.Add(packfile.SystemFilePath);
                    mergedPackFile.MergePackFileContainer(packfile);
                }
            }

            return mergedPackFile;
        }
    }
}
