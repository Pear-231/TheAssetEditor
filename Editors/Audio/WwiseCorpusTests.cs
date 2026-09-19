using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Wem.V132;
using System.Text;
using Editors.Audio.Shared.Wwise;

namespace Test.Audio
{
    public class WwiseCorpusTests
    {
        [Test]
        [Explicit("Builds the resumable complete Warhammer III WEM source and unique-content audit. Set ASSETEDITOR_WH3_DATA_DIR when required.")]
        public void EveryWarhammerThreeWemIsInventoriedAndAudited()
        {
            var gameDataDirectory = ResolveWarhammer3DataDirectory();
            if (gameDataDirectory == null)
                Assert.Ignore("WH3 data directory was not found. Set ASSETEDITOR_WH3_DATA_DIR or install the game.");

            var result = WemCorpusAudit.Run(gameDataDirectory);
            TestContext.Progress.WriteLine($"WH3 WEM audit: sources={result.Sources.Count}, unique={result.UniqueContent.Count}, conflicts={result.Conflicts.Count}, failures={result.Failures.Count}, fingerprint={result.Fingerprint}");
            Assert.That(result.Sources, Is.Not.Empty);
            Assert.That(result.Failures, Is.Empty, string.Join(Environment.NewLine, result.Failures));
            var exceptions = result.UniqueContent.Count(content => content.Classification == "supported-v132-vorbis" && !content.RoundTrips);
            TestContext.Progress.WriteLine($"Supported V132 round-trip exceptions documented in the audit: {exceptions}");
        }

        [Test]
        [Explicit("Builds the initial complete Phase 18 Warhammer III BNK/HIRC audit. Set ASSETEDITOR_WH3_DATA_DIR when required.")]
        public void EveryWarhammerThreeBankAndHircIsInventoriedForPhase18()
        {
            var gameDataDirectory = ResolveWarhammer3DataDirectory();
            if (gameDataDirectory == null)
                Assert.Ignore("WH3 data directory was not found. Set ASSETEDITOR_WH3_DATA_DIR or install the game.");

            var result = BnkCorpusAudit.Run("wh3", gameDataDirectory);
            TestContext.Progress.WriteLine($"WH3 Phase 18 BNK/HIRC audit: sources={result.SourceCount}, effective={result.EffectiveCount}, HIRCs={result.HircCount}, failures={result.Failures.Count}, fingerprint={result.Fingerprint}");
            Assert.That(result.SourceCount, Is.GreaterThan(0));
            Assert.That(result.Failures, Is.Empty, string.Join(Environment.NewLine, result.Failures));

            // WEM source enumeration and content examination remain in the resumable Phase 17
            // ledger. Phase 18 runs it as part of the same WH3 initial-evidence cycle rather than
            // creating a second, incompatible media inventory.
            var wemResult = WemCorpusAudit.Run(gameDataDirectory);
            TestContext.Progress.WriteLine($"WH3 Phase 18 WEM evidence: sources={wemResult.Sources.Count}, unique={wemResult.UniqueContent.Count}, failures={wemResult.Failures.Count}, fingerprint={wemResult.Fingerprint}");
            Assert.That(wemResult.Failures, Is.Empty, string.Join(Environment.NewLine, wemResult.Failures));
        }

        [Test]
        [Explicit("Builds the initial complete Phase 18 Attila BNK/HIRC audit. Set ASSETEDITOR_ATTILA_DATA_DIR when required.")]
        public void EveryAttilaBankAndHircIsInventoriedForPhase18()
        {
            var gameDataDirectory = ResolveAttilaDataDirectory();
            if (gameDataDirectory == null)
                Assert.Ignore("Attila data directory was not found. Set ASSETEDITOR_ATTILA_DATA_DIR or install the game.");

            var result = BnkCorpusAudit.Run("attila", gameDataDirectory);
            TestContext.Progress.WriteLine($"Attila Phase 18 BNK/HIRC audit: sources={result.SourceCount}, effective={result.EffectiveCount}, HIRCs={result.HircCount}, failures={result.Failures.Count}, fingerprint={result.Fingerprint}");
            Assert.That(result.SourceCount, Is.GreaterThan(0));
            Assert.That(result.Failures, Is.Empty, string.Join(Environment.NewLine, result.Failures));

            // Attila WEMs are explicitly out of Phase 18's scope (its complete-cache WEM audit is
            // WH3-only), so this test does not run the WEM ledger the WH3 test reuses from Phase 17.
        }

        [Test]
        [Explicit("Builds the Phase 18 Warhammer III dialogue-event and action-event traversal audit. Set ASSETEDITOR_WH3_DATA_DIR when required.")]
        public void EveryWarhammerThreeDialogueAndActionEventIsWalkedForPhase18()
        {
            var gameDataDirectory = ResolveWarhammer3DataDirectory();
            if (gameDataDirectory == null)
                Assert.Ignore("WH3 data directory was not found. Set ASSETEDITOR_WH3_DATA_DIR or install the game.");

            AssertTraversal(TraversalCorpusAudit.Run("wh3", gameDataDirectory), "WH3");
        }

        [Test]
        [Explicit("Builds the Phase 18 Attila dialogue-event and action-event traversal audit. Set ASSETEDITOR_ATTILA_DATA_DIR when required.")]
        public void EveryAttilaDialogueAndActionEventIsWalkedForPhase18()
        {
            var gameDataDirectory = ResolveAttilaDataDirectory();
            if (gameDataDirectory == null)
                Assert.Ignore("Attila data directory was not found. Set ASSETEDITOR_ATTILA_DATA_DIR or install the game.");

            AssertTraversal(TraversalCorpusAudit.Run("attila", gameDataDirectory), "Attila");
        }

        [Test]
        [Explicit("Reads every indexed file in every installed supported game's pack corpus. Set ASSETEDITOR_*_DATA_DIR to override paths.")]
        public void EveryInstalledSupportedGameFileCanBeDecryptedForPhase18()
        {
            var games = new[]
            {
                new EncryptionCorpusAudit.GameInput("Warhammer", ResolveDataDirectory("ASSETEDITOR_WH1_DATA_DIR", "Total War WARHAMMER", "data"), GameTypeEnum.Warhammer),
                new EncryptionCorpusAudit.GameInput("Warhammer II", ResolveDataDirectory("ASSETEDITOR_WH2_DATA_DIR", "Total War WARHAMMER II", "data"), GameTypeEnum.Warhammer2),
                new EncryptionCorpusAudit.GameInput("Warhammer III", ResolveDataDirectory("ASSETEDITOR_WH3_DATA_DIR", "Total War WARHAMMER III", "data"), GameTypeEnum.Warhammer3),
                new EncryptionCorpusAudit.GameInput("Attila", ResolveDataDirectory("ASSETEDITOR_ATTILA_DATA_DIR", "Total War Attila", "data"), GameTypeEnum.Attila),
                new EncryptionCorpusAudit.GameInput("Rome II", ResolveDataDirectory("ASSETEDITOR_ROME2_DATA_DIR", "Total War Rome II", "data"), GameTypeEnum.Rome2),
                new EncryptionCorpusAudit.GameInput("Rome Remastered", ResolveDataDirectory("ASSETEDITOR_ROMEREMASTERED_DATA_DIR", "Total War ROME REMASTERED", "Contents\\Resources\\Data"), GameTypeEnum.RomeRemastered),
                new EncryptionCorpusAudit.GameInput("Three Kingdoms", ResolveDataDirectory("ASSETEDITOR_3K_DATA_DIR", "Total War THREE KINGDOMS", "data"), GameTypeEnum.ThreeKingdoms),
                new EncryptionCorpusAudit.GameInput("Troy", ResolveDataDirectory("ASSETEDITOR_TROY_DATA_DIR", "Total War Saga Troy", "data"), GameTypeEnum.Troy),
                new EncryptionCorpusAudit.GameInput("Pharaoh", ResolveDataDirectory("ASSETEDITOR_PHARAOH_DATA_DIR", "Total War PHARAOH", "data"), GameTypeEnum.Pharaoh),
                // Pharaoh Dynasties has no distinct GameTypeEnum entry; it shares Pharaoh's pack generation
                // and measured keystream, so it is stamped as Pharaoh rather than left to the fallback rule.
                new EncryptionCorpusAudit.GameInput("Pharaoh Dynasties", ResolveDataDirectory("ASSETEDITOR_PHARAOH_DYNASTIES_DATA_DIR", "Total War PHARAOH DYNASTIES", "data"), GameTypeEnum.Pharaoh)
            };

            var results = EncryptionCorpusAudit.Run(games);
            foreach (var result in results)
            {
                var packs = result.Packs;
                TestContext.Progress.WriteLine($"{result.Game}: packs={packs.Count}, files={packs.Sum(x => x.Files)}, encrypted={packs.Sum(x => x.EncryptedFiles)}, failures={packs.Sum(x => x.Failures.Count) + packs.Count(x => x.LoadError != null)}, fingerprint={result.Fingerprint}");
            }

            Assert.That(results, Has.All.Matches<EncryptionCorpusAudit.GameResult>(result => result.Packs.All(pack => pack.LoadError == null && pack.Failures.Count == 0)));
        }

        // The traversal audit reports findings rather than throwing, exactly as the BNK/HIRC audit does,
        // so only a bank that could not be read at all fails the run. Cycles, depth-capped walks and
        // unresolved targets are evidence for review, not test failures.
        private static void AssertTraversal(TraversalCorpusAudit.AuditResult result, string label)
        {
            var dialogue = result.DialogueEvents;
            var events = result.ActionEvents;
            TestContext.Progress.WriteLine($"{label} Phase 18 traversal audit: fingerprint={result.Fingerprint}, objectIds={result.ObjectIdCount}, bankFailures={result.Failures.Count}");
            TestContext.Progress.WriteLine($"  dialogue events walked={dialogue.Count}, terminated={dialogue.Count(x => x.Terminates)}, cycles={dialogue.Count(x => x.CycleDetected)}, depth-capped={dialogue.Count(x => x.DepthExceeded)}");
            TestContext.Progress.WriteLine($"  leaves={dialogue.Sum(x => x.LeavesReached)}, zero-target leaves={dialogue.Sum(x => x.LeavesWithZeroTarget)}, unresolved-target leaves={dialogue.Sum(x => x.LeavesWithUnresolvedTarget)}");
            TestContext.Progress.WriteLine($"  unreachable-node trees={dialogue.Count(x => x.FlatNodeCount >= 0 && x.NodesReached != x.FlatNodeCount)}");
            TestContext.Progress.WriteLine($"  action events walked={events.Count}, clean={events.Count(x => x.IsClean)}, cycles={events.Count(x => x.CycleDetected)}, depth-capped={events.Count(x => x.DepthExceeded)}, unresolved targets={events.Sum(x => x.TargetsUnresolved)}");

            Assert.That(dialogue.Count + events.Count, Is.GreaterThan(0));
            Assert.That(result.Failures, Is.Empty, string.Join(Environment.NewLine, result.Failures));
        }

        [Test]
        [Explicit("Reads the installed games' complete bank corpora.")]
        public void EverySupportedHircConsumesItsObjectAndEveryWriterRoundTrips()
        {
            var corpora = new[]
            {
                ("Warhammer III", FindPack("Total War WARHAMMER III", "audio_base_bnk.pack")),
                ("Attila", FindPack("Total War Attila", "sound.pack"))
            };

            var failures = new List<string>();
            foreach (var (name, packPath) in corpora)
                failures.AddRange(Audit(name, packPath));

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        private static IReadOnlyList<string> Audit(string name, string packPath)
        {
            using var stream = File.OpenRead(packPath);
            using var reader = new BinaryReader(stream);
            var loader = typeof(PackFileVersionConverter).Assembly
                .GetType("Shared.Core.PackFiles.Serialization.PackFileSerializerLoader", throwOnError: true)!;
            var load = loader.GetMethod("Load", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            var pack = (IPackFileContainer)load.Invoke(null, [packPath, stream.Length, reader, new CaPackDuplicateFileResolver()])!;
            var totals = new Dictionary<string, Result>();
            var bankCount = 0;
            var failures = new List<string>();
            var wemResult = new WemResult();

            foreach (var (path, file) in pack.GetAllFiles().Where(entry => entry.Key.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase)))
            {
                var bytes = file.DataSource.ReadData();
                var bank = BnkFile.CreateFromBytes(bytes, path, isCA: true);
                bankCount++;
                if (bank.DidxChunk != null && bank.DataChunk != null)
                {
                    foreach (var media in bank.DidxChunk.MediaList)
                    {
                        var wemBytes = bank.DataChunk.Data.GetBytesFromBuffer(checked((int)media.Offset), checked((int)media.Size));
                        AuditWem(name, $"{path}:{media.Id}", wemBytes, wemResult, failures);
                    }
                }

                if (bank.HircChunk == null)
                    continue;

                foreach (var hirc in bank.HircChunk.HircItems)
                {
                    var type = hirc is UnknownHircItem ? $"Unknown:{hirc.HircType}" : hirc.GetType().Name;
                    if (!totals.TryGetValue(type, out var result))
                        totals[type] = result = new Result();
                    result.Count++;
                    if (hirc.UnreadByteCount > 0)
                    {
                        result.UnderReadCount++;
                        result.WorstUnderRead = Math.Max(result.WorstUnderRead, hirc.UnreadByteCount);
                    }
                    else if (hirc.UnreadByteCount < 0)
                    {
                        result.OverReadCount++;
                        result.WorstOverRead = Math.Min(result.WorstOverRead, hirc.UnreadByteCount);
                    }

                    if (hirc.UnreadByteCount != 0)
                    {
                        var detail = hirc is ICAkAction action ? $", action={action.GetActionType()}" : string.Empty;
                        failures.Add($"{name}: {path}, {type} {hirc.Id}{detail}, unread={hirc.UnreadByteCount}");
                    }

                    if (hirc is UnknownHircItem)
                    {
                        var unknown = (UnknownHircItem)hirc;
                        if (!string.IsNullOrEmpty(unknown.ErrorMsg))
                        {
                            TestContext.Progress.WriteLine($"{name}: {path}, unknown {hirc.HircType} {hirc.Id}: {unknown.ErrorMsg}");
                        }
                        continue;
                    }

                    try
                    {
                        var original = bytes.AsSpan((int)hirc.ByteIndexInFile, checked((int)(HircHeader.PrefixSize + hirc.SectionSize)));
                        hirc.UpdateSectionSize();
                        var written = hirc.WriteData();
                        result.WritableCount++;
                        if (!original.SequenceEqual(written))
                        {
                            result.RoundTripFailures++;
                            failures.Add($"{name}: {path}, {type} {hirc.Id}, writer differs from source bytes");
                        }
                    }
                    catch (NotSupportedException)
                    {
                    }
                }
            }

            foreach (var (path, file) in pack.GetAllFiles().Where(entry => entry.Key.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)))
                AuditWem(name, path, file.DataSource.ReadData(), wemResult, failures);

            TestContext.Progress.WriteLine($"{name}: {bankCount} banks");
            foreach (var (type, result) in totals.OrderBy(entry => entry.Key))
                TestContext.Progress.WriteLine($"{type}: count={result.Count}, under={result.UnderReadCount}, over={result.OverReadCount}, worst-under={result.WorstUnderRead}, worst-over={result.WorstOverRead}, writable={result.WritableCount}, round-trip-failures={result.RoundTripFailures}");
            TestContext.Progress.WriteLine($"WEM: count={wemResult.Count}, V132={wemResult.SupportedCount}, other-version={wemResult.UnsupportedVersionCount}, non-RIFF={wemResult.NonRiffCount}, intentionally-unwritable={wemResult.IntentionallyUnwritableCount}, writable={wemResult.WritableCount}, round-trip-failures={wemResult.RoundTripFailures}");

            return failures;
        }

        private static void AuditWem(string corpus, string path, byte[] bytes, WemResult result, List<string> failures)
        {
            result.Count++;
            if (bytes.Length < 4 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF")
            {
                result.NonRiffCount++;
                return;
            }

            WemFile wem;
            try
            {
                wem = WemFile.CreateFromWemBytes(bytes);
                result.SupportedCount++;
            }
            catch (InvalidDataException exception) when (exception.Message.StartsWith("Only Wwise Vorbis V132 is supported", StringComparison.Ordinal))
            {
                result.UnsupportedVersionCount++;
                return;
            }
            catch (Exception exception)
            {
                failures.Add($"{corpus}: {path}, WEM read failed: {exception.Message}");
                return;
            }

            try
            {
                var written = wem.WriteData();
                result.WritableCount++;
                if (!bytes.SequenceEqual(written))
                {
                    result.RoundTripFailures++;
                    var commonLength = Math.Min(bytes.Length, written.Length);
                    var firstDifference = Enumerable.Range(0, commonLength).FirstOrDefault(index => bytes[index] != written[index], -1);
                    TestContext.Progress.WriteLine($"WEM difference: {corpus}: {path}, source={bytes.Length}, written={written.Length}, first={firstDifference}, chunks=junk:{wem.JunkChunk != null}/akd:{wem.AkdChunk != null}/cue:{wem.CueChunk != null}/smpl:{wem.SmplChunk != null}/unknown:{wem.UnknownChunks.Count}");
                    failures.Add($"{corpus}: {path}, WEM writer differs from source bytes");
                }
            }
            catch (NotSupportedException)
            {
                result.IntentionallyUnwritableCount++;
            }
        }

        private static string FindPack(string gameDirectory, string packName)
        {
            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
            {
                var path = Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common", gameDirectory, "data", packName);
                if (File.Exists(path))
                    return path;
            }

            throw new FileNotFoundException($"Could not find the installed {packName} corpus.");
        }

        private static string ResolveWarhammer3DataDirectory()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("ASSETEDITOR_WH3_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
                return fromEnvironment;

            var candidates = new[]
            {
                @"D:\SteamLibrary\steamapps\common\Total War WARHAMMER III\data",
                @"C:\Program Files (x86)\Steam\steamapps\common\Total War WARHAMMER III\data",
                @"C:\Program Files\Steam\steamapps\common\Total War WARHAMMER III\data"
            };
            return candidates.FirstOrDefault(Directory.Exists);
        }

        private static string ResolveAttilaDataDirectory()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("ASSETEDITOR_ATTILA_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
                return fromEnvironment;

            var candidates = new[]
            {
                @"D:\SteamLibrary\steamapps\common\Total War Attila\data",
                @"C:\Program Files (x86)\Steam\steamapps\common\Total War Attila\data",
                @"C:\Program Files\Steam\steamapps\common\Total War Attila\data"
            };
            return candidates.FirstOrDefault(Directory.Exists);
        }

        private static string ResolveDataDirectory(string environmentVariable, string gameDirectory, string relativePath)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
                return fromEnvironment;

            return DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
                .Select(drive => Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common", gameDirectory, relativePath))
                .FirstOrDefault(Directory.Exists) ?? Path.Combine("<missing>", gameDirectory, relativePath);
        }

        private sealed class Result
        {
            public int Count { get; set; }
            public int UnderReadCount { get; set; }
            public int OverReadCount { get; set; }
            public int WorstUnderRead { get; set; }
            public int WorstOverRead { get; set; }
            public int WritableCount { get; set; }
            public int RoundTripFailures { get; set; }
        }

        private sealed class WemResult
        {
            public int Count { get; set; }
            public int SupportedCount { get; set; }
            public int UnsupportedVersionCount { get; set; }
            public int NonRiffCount { get; set; }
            public int IntentionallyUnwritableCount { get; set; }
            public int WritableCount { get; set; }
            public int RoundTripFailures { get; set; }
        }
    }
}
