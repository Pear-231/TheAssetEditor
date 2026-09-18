using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Wem.V132;

namespace Test.Audio
{
    internal static class WemCorpusAudit
    {
        private const string AuditDirectoryName = "Research\\AiDocumentation";
        private const string MetadataFileName = "sound-engine-phase17-wh3-wem-audit.metadata.json";
        private const string SourcesFileName = "sound-engine-phase17-wh3-wem-sources.jsonl";
        private const string ContentFileName = "sound-engine-phase17-wh3-wem-content.json";
        private const string SummaryFileName = "sound-engine-phase17-wh3-wem-audit.md";

        public static AuditResult Run(string gameDataDirectory)
        {
            var packPaths = GetPackPaths(gameDataDirectory, out var manifestFound)
                .Where(File.Exists)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var fingerprint = ComputeFingerprint(packPaths);
            var outputDirectory = Path.Combine(FindRepositoryRoot(), AuditDirectoryName);
            Directory.CreateDirectory(outputDirectory);

            var sourcePath = Path.Combine(outputDirectory, SourcesFileName);
            var existing = LoadExistingSources(sourcePath, fingerprint);
            var sources = new List<SourceRecord>(existing.Values);
            var failures = new List<string>();
            var contentByHash = new Dictionary<string, ContentRecord>(StringComparer.Ordinal);
            foreach (var source in sources)
                contentByHash.TryAdd(source.Sha256, new ContentRecord { Sha256 = source.Sha256, ByteLength = source.ByteLength, Classification = source.Classification, Detail = source.Detail, Signature = source.Signature, RoundTrips = source.RoundTrips });

            using (var writer = new StreamWriter(sourcePath, existing.Count != 0, new UTF8Encoding(false)))
            {
                writer.AutoFlush = true;
                foreach (var packPath in packPaths)
                    AuditPack(packPath, existing, sources, contentByHash, failures, writer, fingerprint);
            }

            var conflicts = FindConflicts(sources);
            var result = new AuditResult(fingerprint, manifestFound, packPaths, sources, contentByHash.Values.OrderBy(value => value.Sha256, StringComparer.Ordinal).ToList(), conflicts, failures);
            WriteJson(Path.Combine(outputDirectory, MetadataFileName), new Metadata(fingerprint, manifestFound, packPaths, sources.Count, result.UniqueContent.Count, conflicts.Count, failures));
            WriteJson(Path.Combine(outputDirectory, ContentFileName), result.UniqueContent);
            File.WriteAllText(Path.Combine(outputDirectory, SummaryFileName), CreateSummary(result), new UTF8Encoding(false));
            return result;
        }

        private static void AuditPack(string packPath, Dictionary<string, SourceRecord> existing, List<SourceRecord> sources, Dictionary<string, ContentRecord> contentByHash, List<string> failures, StreamWriter writer, string fingerprint)
        {
            var pack = LoadPack(packPath);
            foreach (var (path, file) in pack.GetAllFiles().OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (path.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
                    AddSource(packPath, path, string.Empty, file.DataSource.ReadData(), existing, sources, contentByHash, writer, fingerprint);
                else if (path.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase))
                    AddBankMedia(packPath, path, file.DataSource.ReadData(), existing, sources, contentByHash, failures, writer, fingerprint);
            }
        }

        private static void AddBankMedia(string packPath, string bankPath, byte[] bankBytes, Dictionary<string, SourceRecord> existing, List<SourceRecord> sources, Dictionary<string, ContentRecord> contentByHash, List<string> failures, StreamWriter writer, string fingerprint)
        {
            BnkFile bank;
            try
            {
                bank = BnkFile.CreateFromBytes(bankBytes, bankPath, isCA: true);
            }
            catch (Exception exception)
            {
                failures.Add($"{packPath}: {bankPath}: could not enumerate DIDX/DATA media: {exception.Message}");
                return;
            }

            if (bank.DidxChunk == null || bank.DataChunk == null)
                return;

            foreach (var media in bank.DidxChunk.MediaList)
                AddSource(packPath, bankPath, media.Id.ToString(), bank.DataChunk.Data.GetBytesFromBuffer(checked((int)media.Offset), checked((int)media.Size)), existing, sources, contentByHash, writer, fingerprint);
        }

        private static void AddSource(string packPath, string internalPath, string mediaId, byte[] bytes, Dictionary<string, SourceRecord> existing, List<SourceRecord> sources, Dictionary<string, ContentRecord> contentByHash, StreamWriter writer, string fingerprint)
        {
            var key = $"{packPath}|{internalPath}|{mediaId}";
            if (existing.ContainsKey(key))
                return;

            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!contentByHash.TryGetValue(hash, out var content))
            {
                content = Examine(bytes, hash);
                contentByHash.Add(hash, content);
            }

            var source = new SourceRecord(key, packPath, internalPath, mediaId, bytes.Length, hash, content.Classification, content.Detail, content.Signature, content.RoundTrips);
            sources.Add(source);
            writer.WriteLine(JsonSerializer.Serialize(new PersistedSource(fingerprint, source)));
        }

        private static ContentRecord Examine(byte[] bytes, string hash)
        {
            if (bytes.Length < 12)
                return new ContentRecord { Sha256 = hash, ByteLength = bytes.Length, Classification = "truncated-or-invalid", Detail = "Payload is shorter than a RIFF header.", Signature = $"short:{bytes.Length}" };
            if (!bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8))
                return new ContentRecord { Sha256 = hash, ByteLength = bytes.Length, Classification = "non-riff-indexed-media", Detail = "Payload does not start with RIFF.", Signature = $"non-riff:{Convert.ToHexString(bytes.AsSpan(0, 4))}" };
            if (!bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
                return new ContentRecord { Sha256 = hash, ByteLength = bytes.Length, Classification = "truncated-or-invalid", Detail = "RIFF payload is not WAVE.", Signature = $"riff:{Encoding.ASCII.GetString(bytes, 8, 4)}" };

            var layout = DescribeRiff(bytes);
            try
            {
                var wem = WemFile.CreateFromWemBytes(bytes);
                var roundTrips = false;
                var detail = $"chunks={layout}; format=0x{wem.FmtChunk.FormatTag:X4}; channels={wem.FmtChunk.Channels}; sample-rate={wem.FmtChunk.SampleRate}; bits={wem.FmtChunk.BitsPerSample}; forward-loop={wem.SmplChunk?.HasForwardLoop ?? false}; unknown={wem.UnknownChunks.Count}; terminal-odd-padding={HasTerminalOddPadding(bytes)}";
                try
                {
                    roundTrips = bytes.SequenceEqual(wem.WriteData());
                    if (!roundTrips)
                        detail += "; writer differs from source";
                }
                catch (NotSupportedException exception)
                {
                    detail += $"; writer unsupported: {exception.Message}";
                }
                return new ContentRecord { Sha256 = hash, ByteLength = bytes.Length, Classification = "supported-v132-vorbis", Detail = detail, Signature = $"v132|{layout}|fmt:{wem.FmtChunk.FormatTag:X4}/{wem.FmtChunk.Channels}/{wem.FmtChunk.SampleRate}/{wem.FmtChunk.BitsPerSample}", RoundTrips = roundTrips };
            }
            catch (InvalidDataException exception) when (exception.Message.StartsWith("Only Wwise Vorbis V132 is supported", StringComparison.Ordinal))
            {
                return new ContentRecord { Sha256 = hash, ByteLength = bytes.Length, Classification = "other-identifiable-wwise-version-or-codec", Detail = exception.Message, Signature = $"unsupported|{layout}" };
            }
            catch (Exception exception)
            {
                return new ContentRecord { Sha256 = hash, ByteLength = bytes.Length, Classification = "truncated-or-invalid", Detail = exception.Message, Signature = $"invalid|{layout}" };
            }
        }

        private static string DescribeRiff(byte[] bytes)
        {
            var chunks = new List<string>();
            var offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                var tag = Encoding.ASCII.GetString(bytes, offset, 4);
                var size = BitConverter.ToUInt32(bytes, offset + 4);
                if (size > bytes.Length - offset - 8)
                    return string.Join(",", chunks) + $",{tag}:truncated";
                chunks.Add($"{tag}:{size}");
                offset += checked((int)(8 + size + (size % 2)));
                if (offset > bytes.Length)
                    return string.Join(",", chunks) + ",padding:truncated";
            }
            return string.Join(",", chunks);
        }

        private static bool HasTerminalOddPadding(byte[] bytes)
        {
            var offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                var size = BitConverter.ToUInt32(bytes, offset + 4);
                var end = offset + 8 + checked((int)size);
                if (end > bytes.Length)
                    return false;
                if (end == bytes.Length)
                    return size % 2 != 0;
                offset = end + (int)(size % 2);
            }
            return false;
        }

        private static IPackFileContainer LoadPack(string packPath)
        {
            using var stream = File.OpenRead(packPath);
            using var reader = new BinaryReader(stream);
            var loader = typeof(PackFileVersionConverter).Assembly.GetType("Shared.Core.PackFiles.Serialization.PackFileSerializerLoader", true)!;
            var load = loader.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!;
            return (IPackFileContainer)load.Invoke(null, [packPath, stream.Length, reader, new CaPackDuplicateFileResolver()])!;
        }

        private static Dictionary<string, SourceRecord> LoadExistingSources(string sourcePath, string fingerprint)
        {
            if (!File.Exists(sourcePath))
                return [];
            var result = new Dictionary<string, SourceRecord>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(sourcePath))
            {
                var persisted = JsonSerializer.Deserialize<PersistedSource>(line);
                if (persisted?.Fingerprint == fingerprint && persisted.Source != null)
                    result.TryAdd(persisted.Source.Key, persisted.Source);
            }
            return result;
        }

        private static List<Conflict> FindConflicts(IEnumerable<SourceRecord> sources)
        {
            return sources.GroupBy(source => string.IsNullOrEmpty(source.MediaId) ? $"path:{source.InternalPath}" : $"media:{source.MediaId}", StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(source => source.Sha256).Distinct(StringComparer.Ordinal).Count() > 1)
                .Select(group => new Conflict(group.Key, group.Select(source => source.Key).OrderBy(key => key, StringComparer.Ordinal).ToList()))
                .OrderBy(conflict => conflict.LogicalKey, StringComparer.Ordinal)
                .ToList();
        }

        private static string ComputeFingerprint(IEnumerable<string> packPaths)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var path in packPaths)
            {
                var info = new FileInfo(path);
                hash.AppendData(Encoding.UTF8.GetBytes($"{Path.GetFullPath(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n"));
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        private static IReadOnlyList<string> GetPackPaths(string gameDataDirectory, out bool manifestFound)
        {
            var manifestPath = Path.Combine(gameDataDirectory, "manifest.txt");
            if (!File.Exists(manifestPath))
            {
                manifestFound = false;
                return Directory.EnumerateFiles(gameDataDirectory, "*.pack").ToList();
            }

            manifestFound = true;
            return File.ReadLines(manifestPath)
                .Select(line => line.Split('\t')[0].Trim())
                .Where(path => path.EndsWith(".pack", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.Combine(gameDataDirectory, path))
                .ToList();
        }

        private static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

        private static string CreateSummary(AuditResult result)
        {
            var classifications = result.UniqueContent.GroupBy(content => content.Classification).OrderBy(group => group.Key, StringComparer.Ordinal);
            var summary = new StringBuilder();
            summary.AppendLine("# Phase 17 - Warhammer III WEM audit");
            summary.AppendLine();
            summary.AppendLine($"Cache fingerprint: `{result.Fingerprint}`. Manifest used: `{result.ManifestFound}`. Contributing packs: {result.PackPaths.Count}.");
            summary.AppendLine();
            summary.AppendLine($"Sources: {result.Sources.Count}; unique payloads: {result.UniqueContent.Count}; source conflicts: {result.Conflicts.Count}; enumeration failures: {result.Failures.Count}.");
            summary.AppendLine();
            summary.AppendLine("| Classification | Unique payloads |");
            summary.AppendLine("| --- | ---: |");
            foreach (var classification in classifications)
                summary.AppendLine($"| {classification.Key} | {classification.Count()} |");
            summary.AppendLine();
            var roundTripExceptions = result.UniqueContent.Where(content => content.Classification == "supported-v132-vorbis" && !content.RoundTrips).ToList();
            summary.AppendLine($"Supported V132 round-trip exceptions: {roundTripExceptions.Count}. These are retained as explicit writer limitations, not counted as successful reproductions.");
            summary.AppendLine();
            summary.AppendLine("| Representative source | Structural signature | Rejection or writer limitation |");
            summary.AppendLine("| --- | --- | --- |");
            foreach (var content in roundTripExceptions.Take(20))
            {
                var source = result.Sources.First(source => source.Sha256 == content.Sha256);
                summary.AppendLine($"| {EscapeMarkdown(source.PackPath + ":" + source.InternalPath + ":" + source.MediaId)} | {EscapeMarkdown(content.Signature)} | {EscapeMarkdown(content.Detail)} |");
            }
            summary.AppendLine();
            summary.AppendLine("The source ledger is `sound-engine-phase17-wh3-wem-sources.jsonl`; unique-content results are `sound-engine-phase17-wh3-wem-content.json`. Both are keyed by the fingerprint above, so a matching rerun resumes completed source locations.");
            return summary.ToString();
        }

        private static string EscapeMarkdown(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                if (directory.EnumerateFiles("*.sln").Any())
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not find the repository root for the Phase 17 audit output.");
        }

        internal sealed record AuditResult(string Fingerprint, bool ManifestFound, IReadOnlyList<string> PackPaths, IReadOnlyList<SourceRecord> Sources, IReadOnlyList<ContentRecord> UniqueContent, IReadOnlyList<Conflict> Conflicts, IReadOnlyList<string> Failures);
        internal sealed record SourceRecord(string Key, string PackPath, string InternalPath, string MediaId, int ByteLength, string Sha256, string Classification, string Detail, string Signature, bool RoundTrips);
        internal sealed class ContentRecord { public string Sha256 { get; set; } = string.Empty; public int ByteLength { get; set; } public string Classification { get; set; } = string.Empty; public string Detail { get; set; } = string.Empty; public string Signature { get; set; } = string.Empty; public bool RoundTrips { get; set; } }
        internal sealed record Conflict(string LogicalKey, IReadOnlyList<string> Sources);
        private sealed record PersistedSource(string Fingerprint, SourceRecord Source);
        private sealed record Metadata(string Fingerprint, bool ManifestFound, IReadOnlyList<string> PackPaths, int SourceCount, int UniqueContentCount, int ConflictCount, IReadOnlyList<string> Failures);
    }
}
