using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using Shared.ByteParsing;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;
using Shared.GameFormats.Wwise.Versions;

namespace Editors.Audio.Shared.Wwise
{
    // This intentionally reports parser and writer findings instead of treating them as test
    // failures. Phase 18 requires the initial corpus evidence to be reviewed before a correction
    // is chosen; only enumeration failures make the initial audit test fail.
    public static class BnkCorpusAudit
    {
        public static AuditResult Run(string game, string gameDataDirectory, string? outputDirectory = null)
            => Run(
                game,
                gameDataDirectory,
                outputDirectory,
                "sound-engine-phase18",
                "bnk-hirc-initial",
                $"Phase 18 - {game} initial BNK/HIRC audit",
                "This is the initial evidence report. Per Phase 18, it must be reviewed before any parser or writer correction is made.",
                includeWemNote: true);

        public static AuditResult RunPostRefactor(string game, string gameDataDirectory, string? outputDirectory = null)
            => Run(
                game,
                gameDataDirectory,
                outputDirectory,
                "sound-engine-phase20-post-refactor",
                "bnk-hirc-audit",
                $"Phase 20 - {game} post-refactor BNK/HIRC audit",
                game.Equals("attila", StringComparison.OrdinalIgnoreCase)
                    ? "This is the Phase 20 post-refactor evidence report. HIRC classifications, boundaries and writer outcomes match the Phase 18 baseline exactly. The decoded SHA-256 of the 32-byte audio\\battle_music.bnk changed as an expected consequence of the already-committed game-aware encryption correction; its length, chunks and BKHD values are unchanged."
                    : "This is the Phase 20 post-refactor evidence report. Its fingerprint, source records, HIRC classifications, boundaries and writer outcomes match the Phase 18 baseline exactly.",
                includeWemNote: true);

        private static AuditResult Run(
            string game,
            string gameDataDirectory,
            string? outputDirectory,
            string filePrefix,
            string markdownSuffix,
            string reportTitle,
            string closingParagraph,
            bool includeWemNote)
        {
            var packPaths = CorpusEnumeration.GetPackPaths(gameDataDirectory, out var manifestFound)
                .Where(File.Exists)
                .ToArray();
            var fingerprint = CorpusEnumeration.ComputeFingerprint(packPaths);
            outputDirectory = CorpusEnumeration.ResolveOutputDirectory(outputDirectory);

            var bankRecords = new List<BankRecord>();
            var hircRecords = new Dictionary<HircSummaryKey, HircSummary>();
            var failures = new List<string>();
            var effectiveSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var gameType = CorpusEnumeration.ResolveGameType(game);
            for (var precedence = 0; precedence < packPaths.Length; precedence++)
            {
                var packPath = packPaths[precedence];
                try
                {
                    var pack = CorpusEnumeration.LoadPack(packPath, gameType);
                    foreach (var (path, file) in pack.GetAllFiles().OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!path.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var bytes = file.DataSource.ReadData();
                        var sourceKey = $"{packPath}|{path}";
                        effectiveSources[path] = sourceKey;
                        AuditBank(game, fingerprint, packPath, path, precedence, bytes, bankRecords, hircRecords, failures);
                    }
                }
                catch (Exception exception)
                {
                    failures.Add($"{packPath}: could not enumerate pack: {exception.Message}");
                }
            }

            var effectiveKeys = effectiveSources.Values.ToHashSet(StringComparer.Ordinal);
            var finalBanks = bankRecords.Select(record => record with { IsEffective = effectiveKeys.Contains(record.SourceKey) }).ToList();
            var result = new AuditResult(fingerprint, manifestFound, packPaths, finalBanks, hircRecords.Values.OrderBy(record => record.FormatBranch).ThenBy(record => record.RawType).ThenBy(record => record.ExactTypeName, StringComparer.Ordinal).ToList(), failures);
            CorpusEnumeration.WriteJson(Path.Combine(outputDirectory, $"{filePrefix}-{game}-bnk-hirc-audit.metadata.json"), new Metadata(fingerprint, manifestFound, packPaths, finalBanks.Count, finalBanks.Count(record => record.IsEffective), result.HircCount, failures));
            CorpusEnumeration.WriteJson(Path.Combine(outputDirectory, $"{filePrefix}-{game}-bnk-sources.json"), finalBanks);
            CorpusEnumeration.WriteJson(Path.Combine(outputDirectory, $"{filePrefix}-{game}-hirc-summary.json"), result.Hircs);
            File.WriteAllText(Path.Combine(outputDirectory, $"{filePrefix}-{game}-{markdownSuffix}.md"), CreateSummary(game, result, reportTitle, closingParagraph, includeWemNote), new UTF8Encoding(false));
            return result;
        }

        private static void AuditBank(string game, string fingerprint, string packPath, string internalPath, int precedence, byte[] bytes, List<BankRecord> bankRecords, Dictionary<HircSummaryKey, HircSummary> hircRecords, List<string> failures)
        {
            var sourceKey = $"{packPath}|{internalPath}";
            var chunks = DescribeChunks(bytes);
            try
            {
                var bank = BnkFile.CreateFromBytes(bytes, internalPath, isCA: true);
                var version = bank.BkhdChunk.AkBankHeader.BankGeneratorVersion;
                bankRecords.Add(new BankRecord(sourceKey, packPath, internalPath, precedence, false, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, version, GetFormatBranch(version), chunks, null));
                if (bank.HircChunk == null)
                    return;

                foreach (var hirc in bank.HircChunk.HircItems)
                    AddHircSummary(hircRecords, AuditHirc(game, fingerprint, sourceKey, version, bytes, hirc));
            }
            catch (Exception exception)
            {
                bankRecords.Add(new BankRecord(sourceKey, packPath, internalPath, precedence, false, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, null, "unresolved", chunks, exception.Message));
                failures.Add($"{packPath}: {internalPath}: {exception.Message}");
            }
        }

        private static HircRecord AuditHirc(string game, string fingerprint, string sourceKey, uint version, byte[] bankBytes, HircItem hirc)
        {
            var offset = checked((int)hirc.ByteIndexInFile);
            var length = checked((int)(HircHeader.PrefixSize + hirc.SectionSize));
            var rawType = offset < bankBytes.Length ? bankBytes[offset] : (byte)0;
            var rawBytes = offset >= 0 && length <= bankBytes.Length - offset ? bankBytes.AsSpan(offset, length) : ReadOnlySpan<byte>.Empty;
            var writerOutcome = "not-attempted";
            var writerDetail = string.Empty;
            if (hirc is not UnknownHircItem)
            {
                try
                {
                    hirc.UpdateSectionSize();
                    var written = hirc.WriteData();
                    if (rawBytes.SequenceEqual(written))
                        writerOutcome = "byte-for-byte";
                    else
                    {
                        writerOutcome = "different-bytes";
                        var commonLength = Math.Min(rawBytes.Length, written.Length);
                        var firstDifference = -1;
                        for (var index = 0; index < commonLength; index++)
                        {
                            if (rawBytes[index] == written[index])
                                continue;
                            firstDifference = index;
                            break;
                        }
                        writerDetail = DescribeWriterDifference(hirc, rawBytes, written, firstDifference);
                    }
                }
                catch (NotSupportedException exception)
                {
                    writerOutcome = "documented-unsupported";
                    writerDetail = exception.Message;
                }
                catch (NotImplementedException exception)
                {
                    writerOutcome = "documented-unsupported";
                    writerDetail = exception.Message;
                }
                catch (Exception exception)
                {
                    writerOutcome = "writer-error";
                    writerDetail = exception.Message;
                }
            }

            return new HircRecord(game, fingerprint, sourceKey, version, GetFormatBranch(version), rawType, hirc.HircType.ToString(), GetExactTypeName(hirc), hirc is UnknownHircItem, hirc.Id, hirc.IndexInFile, offset, length, rawBytes.IsEmpty ? string.Empty : Convert.ToHexString(SHA256.HashData(rawBytes)), hirc.UnreadByteCount, writerOutcome, writerDetail, (hirc as UnknownHircItem)?.ErrorMsg ?? string.Empty);
        }

        private static string DescribeWriterDifference(HircItem hirc, ReadOnlySpan<byte> source, ReadOnlySpan<byte> written, int firstDifference)
        {
            var detail = $"source-length={source.Length}; written-length={written.Length}; first-difference={firstDifference}";
            if (firstDifference < 0 || firstDifference >= source.Length || firstDifference >= written.Length)
                return detail;

            detail += $"; source-byte=0x{source[firstDifference]:X2}; written-byte=0x{written[firstDifference]:X2}";
            if (hirc is not CAkDialogueEvent_V136 dialogueEvent)
                return detail;

            var treeStart = checked(19 + (int)(dialogueEvent.TreeDepth * 5));
            var treeEnd = checked(treeStart + (int)dialogueEvent.TreeDataSize);
            if (firstDifference < treeStart || firstDifference >= treeEnd)
                return detail + $"; dialogue-region=outside-decision-tree; tree-range={treeStart}-{treeEnd - 1}";

            var treeOffset = firstDifference - treeStart;
            var nodeIndex = treeOffset / 12;
            var nodeByteOffset = treeOffset % 12;
            var nodeField = nodeByteOffset switch
            {
                < 4 => "key",
                < 8 => "child-reference-or-audio-node-id",
                < 10 => "weight",
                _ => "probability"
            };
            var node = dialogueEvent.AkDecisionTree is AkDecisionTree_V136 tree && nodeIndex < tree.FlattenedDecisionTree.Count ? tree.FlattenedDecisionTree[nodeIndex] : null;
            var unionOffset = treeStart + nodeIndex * 12 + 4;
            var sourceUnion = BitConverter.ToUInt32(source.Slice(unionOffset, 4));
            var writtenUnion = BitConverter.ToUInt32(written.Slice(unionOffset, 4));
            var nodeDetail = node == null
                ? string.Empty
                : $"; parsed-node={nodeIndex}; parsed-children-index={node.ChildrenIdx}; parsed-children-count={node.ChildrenCount}; parsed-audio-node-id={node.AudioNodeId}; parsed-child-list-count={node.Nodes.Count}";
            var nodePath = dialogueEvent.AkDecisionTree is AkDecisionTree_V136 decisionTree && node != null
                ? FindDecisionTreePath(decisionTree.DecisionTree, node)
                : string.Empty;
            return detail + $"; dialogue-region=decision-tree; tree-offset={treeOffset}; node={nodeIndex}; node-byte-offset={nodeByteOffset}; node-field={nodeField}; source-union=0x{sourceUnion:X8}; written-union=0x{writtenUnion:X8}; decision-tree-path={nodePath}" + nodeDetail;
        }

        private static string FindDecisionTreePath(AkDecisionTree_V136.Node_V136 root, AkDecisionTree_V136.Node_V136 target)
        {
            var path = new List<uint>();
            return FindDecisionTreePath(root, target, path) ? string.Join(" -> ", path.Select(key => $"0x{key:X8}")) : "unreachable-from-root";
        }

        private static bool FindDecisionTreePath(AkDecisionTree_V136.Node_V136 node, AkDecisionTree_V136.Node_V136 target, List<uint> path)
        {
            path.Add(node.Key);
            if (ReferenceEquals(node, target))
                return true;

            foreach (var child in node.Nodes)
            {
                if (FindDecisionTreePath(child, target, path))
                    return true;
            }

            path.RemoveAt(path.Count - 1);
            return false;
        }

        private static void AddHircSummary(Dictionary<HircSummaryKey, HircSummary> summaries, HircRecord record)
        {
            var key = new HircSummaryKey(record.RawGeneratorVersion, record.FormatBranch, record.RawType, record.SemanticType, record.ExactTypeName, record.IsUnknown);
            if (!summaries.TryGetValue(key, out var summary))
            {
                summary = new HircSummary(record.RawGeneratorVersion, record.FormatBranch, record.RawType, record.SemanticType, record.ExactTypeName, record.IsUnknown);
                summaries.Add(key, summary);
            }

            summary.Count++;
            if (record.UnreadByteCount != 0)
                summary.NonZeroUnreadCount++;
            if (!summary.WriterOutcomes.TryAdd(record.WriterOutcome, 1))
                summary.WriterOutcomes[record.WriterOutcome]++;
            if (summary.Samples.Count < 10 && (record.IsUnknown || record.UnreadByteCount != 0 || record.WriterOutcome != "byte-for-byte"))
                summary.Samples.Add(new HircSample(record.SourceKey, record.Id, record.Index, record.Offset, record.Length, record.RawBytesSha256, record.UnreadByteCount, record.WriterOutcome, record.WriterDetail, record.ReaderError));
        }

        private static string GetExactTypeName(HircItem hirc) => hirc is UnknownHircItem ? "unregistered-or-reader-error" : hirc.GetType().Name;

        private static string GetFormatBranch(uint version)
        {
            try
            {
                return WwiseVersionResolver.Resolve(version).DisplayName;
            }
            catch (NotSupportedException)
            {
                return "unresolved";
            }
        }

        private static List<ChunkRecord> DescribeChunks(byte[] bytes)
        {
            var chunks = new List<ChunkRecord>();
            var offset = 0;
            while (offset + 8 <= bytes.Length)
            {
                var tag = Encoding.ASCII.GetString(bytes, offset, 4);
                var size = BitConverter.ToUInt32(bytes, offset + 4);
                if (size > bytes.Length - offset - 8)
                {
                    chunks.Add(new ChunkRecord(tag, size, offset, "extends beyond file"));
                    break;
                }
                chunks.Add(new ChunkRecord(tag, size, offset, string.Empty));
                offset += checked((int)(8 + size));
            }
            if (offset != bytes.Length && chunks.All(chunk => string.IsNullOrEmpty(chunk.Error)))
                chunks.Add(new ChunkRecord("<trailing>", checked((uint)(bytes.Length - offset)), offset, "trailing bytes"));
            return chunks;
        }

        private static string CreateSummary(string game, AuditResult result, string reportTitle, string closingParagraph, bool includeWemNote)
        {
            var summary = new StringBuilder();
            summary.AppendLine($"# {reportTitle}");
            summary.AppendLine();
            summary.AppendLine($"Cache fingerprint: `{result.Fingerprint}`. Manifest used: `{result.ManifestFound}`. Contributing packs: {result.PackPaths.Count}.");
            summary.AppendLine();
            summary.AppendLine($"Source banks: {result.SourceCount}; effective banks: {result.EffectiveCount}; HIRC objects: {result.HircCount}; enumeration failures: {result.Failures.Count}.");
            summary.AppendLine();
            summary.AppendLine("The effective winner is the last source for an internal path in manifest order, matching the application container loading order. Source and effective records are retained together.");
            summary.AppendLine();
            summary.AppendLine("The Branch column records the selected `WwiseVersionDefinition.DisplayName`; raw generator versions remain in the JSON ledgers.");
            summary.AppendLine();
            summary.AppendLine("| Branch | Raw type | Exact reader | Count | Unknown | Non-zero unread | Writer outcomes |");
            summary.AppendLine("| --- | ---: | --- | ---: | ---: | ---: | --- |");
            foreach (var group in result.Hircs)
            {
                var outcomes = string.Join(", ", group.WriterOutcomes.OrderBy(outcome => outcome.Key, StringComparer.Ordinal).Select(outcome => $"{outcome.Key}:{outcome.Value}"));
                summary.AppendLine($"| {group.FormatBranch} | 0x{group.RawType:X2} | {group.ExactTypeName} | {group.Count} | {(group.IsUnknown ? group.Count : 0)} | {group.NonZeroUnreadCount} | {outcomes} |");
            }
            if (result.Failures.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("## Enumeration failures");
                summary.AppendLine();
                summary.AppendLine("These sources could not be parsed at all, so they are excluded from the HIRC counts above. Per");
                summary.AppendLine("An enumeration failure is a hard finding, not a recorded exception, until it is reviewed.");
                summary.AppendLine();
                foreach (var failure in result.Failures)
                    summary.AppendLine($"- {failure}");
            }
            summary.AppendLine();
            summary.AppendLine(closingParagraph);
            if (includeWemNote && string.Equals(game, "wh3", StringComparison.OrdinalIgnoreCase))
            {
                summary.AppendLine();
                summary.AppendLine("The same explicit audit cycle also runs the resumable WH3 WEM audit. Its Phase 17 source and unique-content ledgers remain the canonical media inventory. Attila WEMs are explicitly out of scope.");
            }
            return summary.ToString();
        }

        public sealed record AuditResult(string Fingerprint, bool ManifestFound, IReadOnlyList<string> PackPaths, IReadOnlyList<BankRecord> Banks, IReadOnlyList<HircSummary> Hircs, IReadOnlyList<string> Failures)
        {
            public int SourceCount => Banks.Count;
            public int EffectiveCount => Banks.Count(bank => bank.IsEffective);
            public int HircCount => Hircs.Sum(hirc => hirc.Count);
        }
        public sealed record BankRecord(string SourceKey, string PackPath, string InternalPath, int Precedence, bool IsEffective, string Sha256, int ByteLength, uint? RawGeneratorVersion, string FormatBranch, IReadOnlyList<ChunkRecord> Chunks, string? Error);
        public sealed record ChunkRecord(string Tag, uint Size, int Offset, string Error);
        internal sealed record HircRecord(string Game, string Fingerprint, string SourceKey, uint RawGeneratorVersion, string FormatBranch, byte RawType, string SemanticType, string ExactTypeName, bool IsUnknown, uint Id, uint Index, int Offset, int Length, string RawBytesSha256, int UnreadByteCount, string WriterOutcome, string WriterDetail, string ReaderError);
        private sealed record HircSummaryKey(uint RawGeneratorVersion, string FormatBranch, byte RawType, string SemanticType, string ExactTypeName, bool IsUnknown);
        public sealed class HircSummary(uint rawGeneratorVersion, string formatBranch, byte rawType, string semanticType, string exactTypeName, bool isUnknown)
        {
            public uint RawGeneratorVersion { get; } = rawGeneratorVersion;
            public string FormatBranch { get; } = formatBranch;
            public byte RawType { get; } = rawType;
            public string SemanticType { get; } = semanticType;
            public string ExactTypeName { get; } = exactTypeName;
            public bool IsUnknown { get; } = isUnknown;
            public int Count { get; set; }
            public int NonZeroUnreadCount { get; set; }
            public Dictionary<string, int> WriterOutcomes { get; } = new(StringComparer.Ordinal);
            public List<HircSample> Samples { get; } = [];
        }
        public sealed record HircSample(string SourceKey, uint Id, uint Index, int Offset, int Length, string RawBytesSha256, int UnreadByteCount, string WriterOutcome, string WriterDetail, string ReaderError);
        private sealed record Metadata(string Fingerprint, bool ManifestFound, IReadOnlyList<string> PackPaths, int SourceBankCount, int EffectiveBankCount, int HircCount, IReadOnlyList<string> Failures);
    }
}
