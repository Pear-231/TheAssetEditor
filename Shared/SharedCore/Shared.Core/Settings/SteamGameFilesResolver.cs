using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Shared.Core.Settings
{
    public static class SteamGameFilesResolver
    {
        public static bool Resolve(ApplicationSettings settings)
        {
            var directoriesAdded = false;

            foreach (var game in GameInformationDatabase.Games.Values)
            {
                var existing = settings.GameDirectories.FirstOrDefault(x => x.Game == game.Type);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.Path))
                    continue;

                var installPath = TryGuessInstallationLocation(game.Type);
                if (installPath == null)
                    continue;

                // Most Total War games store their pack files in the data
                var dataPath = Path.Combine(installPath, "data");
                if (!Directory.Exists(dataPath))
                    continue;

                var files = Directory.GetFiles(dataPath);
                var packFiles = files.Count(x => Path.GetExtension(x) == ".pack");
                var manifest = files.Count(x => x.Contains("manifest.txt"));
                if (packFiles == 0 && manifest == 0)
                    continue;

                if (existing != null)
                    settings.GameDirectories.Remove(existing);

                settings.GameDirectories.Add(new ApplicationSettings.GamePathPair(game.Type, dataPath));
                directoriesAdded = true;
            }

            return directoriesAdded;
        }

        public static string? TryGuessInstallationLocation(GameTypeEnum game)
        {
            int steamAppId;
            if (game == GameTypeEnum.Warhammer)
                steamAppId = 364360;
            else if (game == GameTypeEnum.Warhammer2)
                steamAppId = 594570;
            else if (game == GameTypeEnum.Warhammer3)
                steamAppId = 1142710;
            else if (game == GameTypeEnum.Troy)
                steamAppId = 1099410;
            else if (game == GameTypeEnum.ThreeKingdoms)
                steamAppId = 779340;
            else if (game == GameTypeEnum.Rome2)
                steamAppId = 214950;
            else if (game == GameTypeEnum.Attila)
                steamAppId = 325610;
            else if (game == GameTypeEnum.Pharaoh)
                steamAppId = 1937780;
            else
                return null;

            try
            {
                var steamPath = GetSteamInstallPath();
                if (steamPath == null)
                    return null;

                foreach (var libraryPath in GetSteamLibraryFolders(steamPath))
                {
                    var manifestPath = Path.Combine(libraryPath, "steamapps", $"appmanifest_{steamAppId}.acf");
                    if (!File.Exists(manifestPath))
                        continue;

                    var installationDirectory = GetSteamInstallDir(manifestPath);
                    if (installationDirectory == null)
                        continue;

                    var installationPath = Path.Combine(libraryPath, "steamapps", "common", installationDirectory);
                    if (Directory.Exists(installationPath))
                        return installationPath;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetSteamInstallPath()
        {
            var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (string.IsNullOrWhiteSpace(path))
                path = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;

            return string.IsNullOrWhiteSpace(path) ? null : path.Replace('/', '\\');
        }

        private static List<string> GetSteamLibraryFolders(string steamPath)
        {
            var libraryFolders = new List<string> { steamPath };

            var libraryFoldersFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersFile))
                return libraryFolders;

            var content = File.ReadAllText(libraryFoldersFile);
            libraryFolders.AddRange(Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value.Replace("\\\\", "\\")));

            return libraryFolders;
        }

        private static string? GetSteamInstallDir(string manifestPath)
        {
            var content = File.ReadAllText(manifestPath);
            var match = Regex.Match(content, "\"installdir\"\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
