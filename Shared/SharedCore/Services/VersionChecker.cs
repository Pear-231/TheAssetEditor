using System.Diagnostics;
using System.Windows;
using Octokit;
using Shared.Core.Misc;
using SharpCompress.Archives;
using SharpCompress.Common;
using FileMode = System.IO.FileMode;

namespace Shared.Core.Services
{
    public class VersionChecker
    {
        public static string CurrentVersion { get => "0.65"; } // TODO: Ensure this is correct
        private const string GitHubOwner = "donkeyProgramming";
        private const string GitHubRepository = "TheAssetEditor";

        public static async Task CheckVersion()
        {
            //if (Debugger.IsAttached)
            //    return;

            var release = await GetLatestReleaseAsync().ConfigureAwait(false);
            if (release != null)
            {
                var currentVersion = $"v{CurrentVersion}";
                if (release.TagName != currentVersion)
                {
                    // Show user update dialog

                    // Download, extract and update
                    var updateDirectory = DirectoryHelper.UpdateDirectory;
                    if (Directory.Exists(updateDirectory))
                        Directory.Delete(updateDirectory, true);
                    Directory.CreateDirectory(updateDirectory);

                    var asset = release.Assets[0];
                    var assetPath = Path.Combine(updateDirectory, asset.Name);
                    var downloadResult = await DownloadAssetAsync(asset.BrowserDownloadUrl, assetPath);
                    if (downloadResult == false)
                        return;

                    ExtractRar(assetPath, updateDirectory);

                    File.Delete(assetPath);

                    var batchPath = Path.Combine(updateDirectory, "version_updater.bat");
                    if (File.Exists(batchPath))
                        File.Delete(batchPath);

                    // The folder within the update directory  contains the actual update files
                    var releaseDirectory = Directory.GetDirectories(updateDirectory)[0];
                    var installationDirectory = AppContext.BaseDirectory;
                    WriteVersionUpdater(batchPath, releaseDirectory, installationDirectory);

                    UpdateVersion(batchPath);
                }
            }
        }

        public static async Task<Release?> GetLatestReleaseAsync()
        {
            try
            {
                var gitHubClient = new GitHubClient(new ProductHeaderValue("AssetEditor_instance"));
                var releases = await gitHubClient.Repository.Release.GetAll(GitHubOwner, GitHubRepository).ConfigureAwait(false);
                var latestRelease = releases[0];
                return latestRelease;
            }
            catch
            {
                MessageBox.Show("Unable to contact Github to check for later version");
                return null;
            }
        }

        public static async Task<bool> DownloadAssetAsync(string downloadUrl, string downloadPath)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AssetEditor_instance");

                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await responseStream.CopyToAsync(fileStream).ConfigureAwait(false);
                return true;
            }
            catch
            {
                MessageBox.Show("Unable to download latest version from Github");
                return false;
            }
        }

        private static void ExtractRar(string rarPath, string extractionDirectory)
        {
            var extension = Path.GetExtension(rarPath);
            if (extension != ".rar")
                throw new InvalidFormatException("Asset is not a RAR.");

            using var rar = ArchiveFactory.OpenArchive(rarPath);
            foreach (var entry in rar.Entries.Where(entry => !entry.IsDirectory))
                entry.WriteToDirectory(extractionDirectory);
        }

        public static void WriteVersionUpdater(string batchPath, string updateDirectory, string installationDirectory)
        {
            var currentExe = Environment.ProcessPath;
            var script = $"""
                @echo off
                timeout /t 2 /nobreak >nul

                rmdir /s /q "{installationDirectory}"
                mkdir "{installationDirectory}"

                xcopy "{updateDirectory}\*" "{installationDirectory}" /s /e /y /q

                start "" "{currentExe}"
                del "%~f0"
                """;
            File.WriteAllText(batchPath, script);
        }

        public static void UpdateVersion(string batchFile)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchFile}\"",
                CreateNoWindow = true,
                UseShellExecute = true,
                Verb = "runas"
            });
            Environment.Exit(0);
        }
    }
}
