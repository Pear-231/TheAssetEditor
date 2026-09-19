using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;

namespace Editors.Audio.Shared.Wwise
{
    // Shared by the Phase 18 audits so every one of them visits the same containers in the same order
    // and reports the same cache fingerprint. A traversal result that cannot be lined up against the
    // BNK/HIRC result it was derived from is not evidence.
    internal static class CorpusEnumeration
    {
        public const string AuditDirectoryName = "Research\\AiDocumentation";

        public static IReadOnlyList<string> GetPackPaths(string gameDataDirectory, out bool manifestFound)
        {
            var manifestPath = Path.Combine(gameDataDirectory, "manifest.txt");
            if (!File.Exists(manifestPath))
            {
                manifestFound = false;
                return Directory.EnumerateFiles(gameDataDirectory, "*.pack").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            }

            manifestFound = true;
            return File.ReadLines(manifestPath)
                .Select(line => line.Split('\t')[0].Trim())
                .Where(path => path.EndsWith(".pack", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.Combine(gameDataDirectory, path))
                .ToList();
        }

        public static string ComputeFingerprint(IEnumerable<string> packPaths)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var path in packPaths)
            {
                var info = new FileInfo(path);
                hash.AppendData(Encoding.UTF8.GetBytes($"{Path.GetFullPath(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n"));
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        public static IPackFileContainer LoadPack(string packPath)
        {
            using var stream = File.OpenRead(packPath);
            using var reader = new BinaryReader(stream);
            var loader = typeof(PackFileVersionConverter).Assembly.GetType("Shared.Core.PackFiles.Serialization.PackFileSerializerLoader", true)!;
            var load = loader.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!;
            return (IPackFileContainer)load.Invoke(null, [packPath, stream.Length, reader, new CaPackDuplicateFileResolver()])!;
        }

        public static void WriteJson<T>(string path, T value)
            => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

        public static string ResolveOutputDirectory(string? outputDirectory)
        {
            outputDirectory ??= Path.Combine(FindRepositoryRoot(), AuditDirectoryName);
            Directory.CreateDirectory(outputDirectory);
            return outputDirectory;
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                if (directory.EnumerateFiles("*.sln").Any())
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not find the repository root for the Phase 18 audit output.");
        }
    }
}
