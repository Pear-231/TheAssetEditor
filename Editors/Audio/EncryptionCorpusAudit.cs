using System.Text;
using System.Text.Json;
using System.IO;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.Settings;

namespace Editors.Audio.Shared.Wwise
{
    // Phase 18 evidence for pack encryption is deliberately separate from the HIRC audit. It reads
    // every indexed file, including files which are not Wwise media, and reads every encrypted entry
    // through the same data-source path the application uses. Unencrypted payloads do not provide
    // encryption evidence, so their bytes are not needlessly copied from the installed games.
    internal static class EncryptionCorpusAudit
    {
        public static IReadOnlyList<GameResult> Run(IReadOnlyList<GameInput> games, string? outputDirectory = null)
        {
            outputDirectory = CorpusEnumeration.ResolveOutputDirectory(outputDirectory);
            var results = games.Select(Audit).ToList();
            CorpusEnumeration.WriteJson(Path.Combine(outputDirectory, "sound-engine-phase18-encryption-corpus.json"), results);
            File.WriteAllText(Path.Combine(outputDirectory, "sound-engine-phase18-encryption-corpus.md"), CreateReport(results), new UTF8Encoding(false));
            return results;
        }

        private static GameResult Audit(GameInput game)
        {
            var manifestFound = false;
            var packPaths = Directory.Exists(game.DataDirectory)
                ? Directory.EnumerateFiles(game.DataDirectory, "*.pack", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
            manifestFound = File.Exists(Path.Combine(game.DataDirectory, "manifest.txt"));
            var fingerprint = CorpusEnumeration.ComputeFingerprint(packPaths);
            var packs = new List<PackResult>();
            var gameType = game.Game ?? throw new InvalidOperationException($"The encryption audit requires a game identity for '{game.Name}'.");

            foreach (var packPath in packPaths)
            {
                var packResult = new PackResult(Path.GetFileName(packPath));
                try
                {
                    var pack = CorpusEnumeration.LoadPack(packPath, gameType);
                    foreach (var (internalPath, file) in pack.GetAllFiles())
                    {
                        packResult.Files++;
                        if (file.DataSource is not PackedFileSource source)
                        {
                            packResult.NonPackedFiles++;
                            continue;
                        }

                        if (source.IsEncrypted)
                        {
                            packResult.EncryptedFiles++;
                            var extension = Path.GetExtension(internalPath);
                            if (string.IsNullOrEmpty(extension))
                                extension = "(none)";
                            packResult.EncryptedExtensionCounts[extension] = packResult.EncryptedExtensionCounts.GetValueOrDefault(extension) + 1;
                        }
                        if (source.IsCompressed)
                            packResult.CompressedFiles++;

                        if (!source.IsEncrypted)
                            continue;

                        packResult.ReadFiles++;
                        try
                        {
                            var bytes = source.ReadData();
                            if (source.IsEncrypted)
                            {
                                packResult.DecryptedBytes += bytes.LongLength;
                                var error = ValidateKnownHeader(internalPath, bytes);
                                if (error != null)
                                    packResult.Failures.Add($"{internalPath}: {error}");
                            }
                        }
                        catch (Exception exception)
                        {
                            packResult.Failures.Add($"{internalPath}: {exception.GetType().Name}: {exception.Message} (game={source.Parent.GameType}, encrypted={source.IsEncrypted}, compressed={source.IsCompressed}, compression={source.CompressionFormat}, stored-size={source.Size}, uncompressed-size={source.UncompressedSize})");
                        }
                    }
                }
                catch (Exception exception)
                {
                    packResult.LoadError = $"{exception.GetType().Name}: {exception.Message}";
                }
                packs.Add(packResult);
            }

            return new GameResult(game.Name, game.DataDirectory, manifestFound, fingerprint, packs);
        }

        private static string? ValidateKnownHeader(string path, byte[] bytes)
        {
            if (bytes.Length == 0)
                return null;

            var extension = Path.GetExtension(path);
            string? expected = extension.ToLowerInvariant() switch
            {
                ".bnk" => "BKHD",
                ".wem" or ".wav" => "RIFF",
                ".ogg" => "OggS",
                _ => null
            };
            if (expected == null || bytes.Length < 4)
                return null;

            var actual = Encoding.ASCII.GetString(bytes, 0, 4);
            return actual == expected ? null : $"expected {expected} header, got {actual}";
        }

        private static string CreateReport(IReadOnlyList<GameResult> results)
        {
            var report = new StringBuilder();
            report.AppendLine("# Phase 18 pack encryption/decryption corpus audit");
            report.AppendLine();
            report.AppendLine("Every indexed file in every discovered pack was enumerated. Every encrypted entry was read through the production data-source path; encrypted known media formats also had their decoded header checked, which is independent of an encrypt/decrypt round trip.");
            report.AppendLine();
            report.AppendLine("| Game | Packs | Files | Encrypted/read | Compressed | Read failures | Load failures | Encrypted extensions | Fingerprint |");
            report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |");
            foreach (var result in results)
            {
                var packs = result.Packs;
                var extensionCounts = new Dictionary<string, int>();
                foreach (var pack in packs)
                    foreach (var (extension, count) in pack.EncryptedExtensionCounts)
                        extensionCounts[extension] = extensionCounts.GetValueOrDefault(extension) + count;
                var extensionSummary = string.Join(", ", extensionCounts.OrderByDescending(x => x.Value).Select(x => $"{x.Key}:{x.Value}"));
                report.AppendLine($"| {result.Game} | {packs.Count} | {packs.Sum(x => x.Files)} | {packs.Sum(x => x.EncryptedFiles)}/{packs.Sum(x => x.ReadFiles)} | {packs.Sum(x => x.CompressedFiles)} | {packs.Sum(x => x.Failures.Count)} | {packs.Count(x => x.LoadError != null)} | {extensionSummary} | `{result.Fingerprint}` |");
                foreach (var pack in packs.Where(x => x.LoadError != null || x.Failures.Count > 0))
                {
                    report.AppendLine();
                    report.AppendLine($"- **{result.Game} / {pack.Pack}**: {pack.LoadError ?? string.Join("; ", pack.Failures.Take(5))}");
                }
            }
            return report.ToString();
        }

        // Game is optional only for titles with no matching GameTypeEnum entry (Pharaoh Dynasties) or no
        // encrypted content at all (Rome Remastered); when set, it is stamped onto every loaded pack's
        // PackedFileSourceParent so decryption uses the measured per-game keystream rather than falling
        // back to the version-only rule that is wrong for Warhammer I. See FileEncryption.BlockKey.
        internal sealed record GameInput(string Name, string DataDirectory, GameTypeEnum? Game = null);
        public sealed record GameResult(string Game, string DataDirectory, bool ManifestFound, string Fingerprint, IReadOnlyList<PackResult> Packs);
        public sealed class PackResult(string pack)
        {
            public string Pack { get; } = pack;
            public int Files { get; set; }
            public int NonPackedFiles { get; set; }
            public int EncryptedFiles { get; set; }
            public int ReadFiles { get; set; }
            public int CompressedFiles { get; set; }
            public long DecryptedBytes { get; set; }
            public string? LoadError { get; set; }
            public List<string> Failures { get; } = [];
            public Dictionary<string, int> EncryptedExtensionCounts { get; } = [];
        }
    }
}
