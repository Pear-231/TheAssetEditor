using System.IO;
using System.Text;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V112.Shared;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;
using static Shared.GameFormats.Wwise.Hirc.ICAkDialogueEvent;

namespace Editors.Audio.Shared.Wwise
{
    // Phase 18's traversal audit. A byte-for-byte round trip proves an object was read, not that what
    // it points at exists or that walking it terminates -- the Audio Explorer stack overflow was
    // exactly that gap. This walks every dialogue-event decision tree and every action-event chain
    // with an explicit cycle and depth guard, and reports what each one resolves to.
    //
    // The walker never throws and never recurses without a bound: a cycle, an over-deep chain or a
    // dangling target is recorded as a finding, because a crashing auditor cannot report on the
    // corpus that crashed it.
    public static class TraversalCorpusAudit
    {
        // A dialogue tree is bounded by its own argument count; an action chain is bounded only by
        // how the content is authored. Both caps exist to turn a runaway walk into a recorded finding.
        private const int MaxDecisionTreeDepth = 32;
        private const int MaxActionChainDepth = 64;

        public static AuditResult Run(string game, string gameDataDirectory, string? outputDirectory = null)
        {
            var packPaths = CorpusEnumeration.GetPackPaths(gameDataDirectory, out var manifestFound).Where(File.Exists).ToArray();
            var fingerprint = CorpusEnumeration.ComputeFingerprint(packPaths);
            outputDirectory = CorpusEnumeration.ResolveOutputDirectory(outputDirectory);

            var corpus = new Corpus();
            var failures = new List<string>();
            var gameType = CorpusEnumeration.ResolveGameType(game);
            foreach (var packPath in packPaths)
            {
                try
                {
                    var pack = CorpusEnumeration.LoadPack(packPath, gameType);
                    foreach (var (path, file) in pack.GetAllFiles().OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!path.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            var bank = BnkFile.CreateFromBytes(file.DataSource.ReadData(), path, isCA: true);
                            corpus.Add($"{packPath}|{path}", bank);
                        }
                        catch (Exception exception)
                        {
                            failures.Add($"{packPath}: {path}: {exception.Message}");
                        }
                    }
                }
                catch (Exception exception)
                {
                    failures.Add($"{packPath}: could not enumerate pack: {exception.Message}");
                }
            }

            var dialogueResults = corpus.DialogueEvents.Select(WalkDialogueEvent).ToList();
            var eventResults = corpus.Events.Select(entry => WalkActionEvent(entry, corpus)).ToList();

            var result = new AuditResult(
                fingerprint, manifestFound, packPaths, failures,
                corpus.ObjectIdCount, dialogueResults, eventResults);

            CorpusEnumeration.WriteJson(Path.Combine(outputDirectory, $"sound-engine-phase18-{game}-traversal-audit.metadata.json"),
                new Metadata(fingerprint, manifestFound, packPaths, corpus.ObjectIdCount, dialogueResults.Count, eventResults.Count, failures));
            CorpusEnumeration.WriteJson(Path.Combine(outputDirectory, $"sound-engine-phase18-{game}-traversal-dialogue-events.json"), dialogueResults);
            CorpusEnumeration.WriteJson(Path.Combine(outputDirectory, $"sound-engine-phase18-{game}-traversal-action-events.json"), eventResults.Where(entry => !entry.IsClean).Take(2000).ToList());
            File.WriteAllText(Path.Combine(outputDirectory, $"sound-engine-phase18-{game}-traversal.md"), CreateSummary(game, result), new UTF8Encoding(false));
            return result;
        }

        // Walks the resolved tree from its root. Guards on both the node instances already seen on the
        // current path (a genuine cycle) and on absolute depth, so a malformed tree is reported rather
        // than run forever.
        private static DialogueEventResult WalkDialogueEvent(DialogueEventEntry entry)
        {
            var root = entry.DialogueEvent.AkDecisionTree.GetDecisionTree();
            var visited = new HashSet<IAkDecisionNode>(ReferenceEqualityComparer.Instance);
            var reached = new HashSet<IAkDecisionNode>(ReferenceEqualityComparer.Instance);
            var leaves = 0;
            var zeroTargets = 0;
            var unresolvedTargets = 0;
            var unresolvedButMedia = 0;
            var sampleUnresolved = new List<uint>();
            var argumentCount = entry.DialogueEvent.Arguments.Count;
            var earlyTerminatedLeaves = 0;
            var unresolvedAtEarlyTerminatedLeaf = 0;
            var maxDepth = 0;
            var cycleDetected = false;
            var depthExceeded = false;

            void Walk(IAkDecisionNode node, int depth)
            {
                if (node == null)
                    return;

                if (depth > MaxDecisionTreeDepth)
                {
                    depthExceeded = true;
                    return;
                }

                if (!visited.Add(node))
                {
                    cycleDetected = true;
                    return;
                }

                reached.Add(node);
                maxDepth = Math.Max(maxDepth, depth);

                var childCount = node.GetChildrenCount();
                if (childCount == 0)
                {
                    leaves++;

                    // A leaf above the tree's own argument count is an early-terminated path. Those are
                    // the only leaves where the reader's plausibility fallback runs instead of the
                    // deterministic at-max-depth rule, so they are the ones a misread could reach --
                    // wwiser documents them existing in this corpus. Split the counts so a target that
                    // fails to resolve can be attributed to the right cause.
                    var isEarlyTerminated = depth < argumentCount;
                    if (isEarlyTerminated)
                        earlyTerminatedLeaves++;

                    var target = node.GetAudioNodeId();
                    if (target == 0)
                        zeroTargets++;
                    else if (!entry.ObjectExists(target))
                    {
                        unresolvedTargets++;
                        if (isEarlyTerminated)
                            unresolvedAtEarlyTerminatedLeaf++;
                        if (entry.MediaExists(target))
                            unresolvedButMedia++;
                        if (sampleUnresolved.Count < 8)
                            sampleUnresolved.Add(target);
                    }
                }
                else
                {
                    for (var i = 0; i < childCount; i++)
                        Walk(node.GetChildAtIndex(i), depth + 1);
                }

                visited.Remove(node);
            }

            Walk(root, 0);

            var flatNodeCount = GetFlatNodeCount(entry.DialogueEvent.AkDecisionTree);
            return new DialogueEventResult(
                entry.SourceKey, entry.Id, entry.ExactTypeName, entry.DialogueEvent.Arguments.Count,
                flatNodeCount, reached.Count, leaves, zeroTargets, unresolvedTargets, unresolvedButMedia,
                earlyTerminatedLeaves, unresolvedAtEarlyTerminatedLeaf, maxDepth,
                !cycleDetected && !depthExceeded, cycleDetected, depthExceeded, sampleUnresolved);
        }

        // The flat list is what gets written back out, so a node the tree walk never reaches is a node
        // whose bytes nothing accounts for. Only the concrete readers expose it.
        private static int GetFlatNodeCount(IAkDecisionTree tree) => tree switch
        {
            AkDecisionTree_V136 v136 => v136.FlattenedDecisionTree.Count,
            AkDecisionTree_V112 v112 => v112.FlattenedDecisionTree.Count,
            _ => -1
        };

        // Walks event -> actions -> each action's target, following targets that are themselves events
        // or dialogue events. Cycles are bounded by the ids already on the current path, which is what
        // the production lazy-loading explorer does and what the eager path does not.
        private static ActionEventResult WalkActionEvent(EventEntry entry, Corpus corpus)
        {
            var onPath = new HashSet<uint>();
            var unresolvedByActionType = new Dictionary<string, int>(StringComparer.Ordinal);
            var actionsSeen = 0;
            var targetsResolved = 0;
            var targetsUnresolved = 0;
            var gameSyncTargets = 0;
            var maxDepth = 0;
            var cycleDetected = false;
            var depthExceeded = false;

            void WalkEvent(uint eventId, int depth)
            {
                if (depth > MaxActionChainDepth)
                {
                    depthExceeded = true;
                    return;
                }

                if (!onPath.Add(eventId))
                {
                    cycleDetected = true;
                    return;
                }

                maxDepth = Math.Max(maxDepth, depth);

                if (corpus.EventActionIds.TryGetValue(eventId, out var actionIds))
                {
                    foreach (var actionId in actionIds)
                    {
                        actionsSeen++;
                        if (!corpus.ActionTargets.TryGetValue(actionId, out var action))
                        {
                            // The event names an action id no action object in the corpus claims.
                            unresolvedByActionType.TryAdd("<action object missing>", 0);
                            unresolvedByActionType["<action object missing>"]++;
                            targetsUnresolved++;
                            continue;
                        }

                        if (action.TargetId == 0)
                            continue;

                        if (!TargetIsHircObject(action.ActionType))
                        {
                            // Names a game sync, not an object. Counted so the total still reconciles
                            // against the actions visited, but it is not an unresolved reference.
                            gameSyncTargets++;
                            continue;
                        }

                        if (!corpus.ObjectExists(action.TargetId))
                        {
                            var bucket = corpus.MediaExists(action.TargetId)
                                ? $"{action.ActionType} (target is a DIDX media id)"
                                : action.ActionType.ToString();
                            unresolvedByActionType.TryAdd(bucket, 0);
                            unresolvedByActionType[bucket]++;
                            targetsUnresolved++;
                            continue;
                        }

                        targetsResolved++;
                        if (corpus.EventActionIds.ContainsKey(action.TargetId) || corpus.DialogueEventIds.Contains(action.TargetId))
                            WalkEvent(action.TargetId, depth + 1);
                    }
                }

                onPath.Remove(eventId);
            }

            WalkEvent(entry.Id, 0);
            return new ActionEventResult(
                entry.SourceKey, entry.Id, actionsSeen, targetsResolved, targetsUnresolved, gameSyncTargets,
                maxDepth, !cycleDetected && !depthExceeded && targetsUnresolved == 0, cycleDetected, depthExceeded,
                unresolvedByActionType);
        }

        private static string CreateSummary(string game, AuditResult result)
        {
            var summary = new StringBuilder();
            summary.AppendLine($"# Phase 18 - {game} dialogue-event and action-event traversal audit");
            summary.AppendLine();
            summary.AppendLine($"Cache fingerprint: `{result.Fingerprint}`. Manifest used: `{result.ManifestFound}`. Contributing packs: {result.PackPaths.Count}.");
            summary.AppendLine();
            summary.AppendLine("A byte-for-byte round trip proves an object was read, not that what it references exists or that");
            summary.AppendLine("walking it terminates. Every walk below is cycle-guarded and depth-bounded; a cycle, an over-deep");
            summary.AppendLine("chain or a dangling target is recorded rather than thrown.");
            summary.AppendLine();
            summary.AppendLine($"Distinct HIRC object ids in corpus: {result.ObjectIdCount}. Bank read failures: {result.Failures.Count}.");
            summary.AppendLine();

            summary.AppendLine("## Dialogue-event decision trees");
            summary.AppendLine();
            var d = result.DialogueEvents;
            summary.AppendLine($"Walked: {d.Count}. Terminated cleanly: {d.Count(x => x.Terminates)}. Cycles: {d.Count(x => x.CycleDetected)}. Depth-capped: {d.Count(x => x.DepthExceeded)}.");
            summary.AppendLine($"Leaves reached: {d.Sum(x => x.LeavesReached)}. Leaves with a zero target: {d.Sum(x => x.LeavesWithZeroTarget)}. Leaves whose target resolves to no object in the corpus: {d.Sum(x => x.LeavesWithUnresolvedTarget)}, of which {d.Sum(x => x.LeavesWhoseTargetIsAMediaId)} name a DIDX media id rather than a HIRC object.");
            summary.AppendLine($"Leaves on an early-terminated path (leaf depth below the tree's own argument count): {d.Sum(x => x.EarlyTerminatedLeaves)}, of which {d.Sum(x => x.UnresolvedAtEarlyTerminatedLeaf)} fail to resolve. These are the only leaves where the reader's plausibility fallback runs rather than the deterministic at-max-depth rule, so they are the only ones a misread could reach.");
            var unreachable = d.Where(x => x.FlatNodeCount >= 0 && x.NodesReached != x.FlatNodeCount).ToList();
            summary.AppendLine($"Trees where the walk did not reach every flat node: {unreachable.Count}.");
            summary.AppendLine();
            if (unreachable.Count > 0)
            {
                summary.AppendLine("| Source | Id | Flat nodes | Reached |");
                summary.AppendLine("| --- | ---: | ---: | ---: |");
                foreach (var item in unreachable.Take(25))
                    summary.AppendLine($"| `{item.SourceKey}` | {item.Id} | {item.FlatNodeCount} | {item.NodesReached} |");
                summary.AppendLine();
            }

            summary.AppendLine("## Action-event chains");
            summary.AppendLine();
            var e = result.ActionEvents;
            summary.AppendLine($"Walked: {e.Count}. Terminated cleanly with every target resolved: {e.Count(x => x.IsClean)}. Cycles: {e.Count(x => x.CycleDetected)}. Depth-capped: {e.Count(x => x.DepthExceeded)}.");
            summary.AppendLine($"Actions visited: {e.Sum(x => x.ActionsVisited)}. Targets resolved: {e.Sum(x => x.TargetsResolved)}. Targets naming a game sync rather than an object (state, switch, RTPC, trigger families): {e.Sum(x => x.GameSyncTargets)}. Targets resolving to no object in the corpus: {e.Sum(x => x.TargetsUnresolved)}.");
            summary.AppendLine($"Deepest chain: {(e.Count == 0 ? 0 : e.Max(x => x.MaxDepth))}.");
            summary.AppendLine();

            var byActionType = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in e)
            {
                foreach (var (bucket, count) in entry.UnresolvedByActionType)
                {
                    byActionType.TryAdd(bucket, 0);
                    byActionType[bucket] += count;
                }
            }
            if (byActionType.Count > 0)
            {
                summary.AppendLine("Unresolved targets by the action type that named them. An action's `IdExt` is only a HIRC");
                summary.AppendLine("object reference for the play/stop families; for the property-setting, bypass and seek families");
                summary.AppendLine("it names whatever that action scopes to, so an entry here is not automatically a dangling");
                summary.AppendLine("reference -- it is a question about what that action type's target field means.");
                summary.AppendLine();
                summary.AppendLine("| Action type | Unresolved targets |");
                summary.AppendLine("| --- | ---: |");
                foreach (var (bucket, count) in byActionType.OrderByDescending(pair => pair.Value))
                    summary.AppendLine($"| {bucket} | {count} |");
                summary.AppendLine();
            }
            var dirty = e.Where(x => !x.IsClean).ToList();
            if (dirty.Count > 0)
            {
                summary.AppendLine("| Source | Event id | Actions | Unresolved | Max depth | Cycle | Depth capped |");
                summary.AppendLine("| --- | ---: | ---: | ---: | ---: | --- | --- |");
                foreach (var item in dirty.Take(25))
                    summary.AppendLine($"| `{item.SourceKey}` | {item.Id} | {item.ActionsVisited} | {item.TargetsUnresolved} | {item.MaxDepth} | {item.CycleDetected} | {item.DepthExceeded} |");
                summary.AppendLine();
            }

            summary.AppendLine("Full per-object results are in the traversal JSON files beside this report. Action-event rows are");
            summary.AppendLine("persisted only where something was found, since a clean chain carries no evidence a count does not.");
            return summary.ToString();
        }

        private sealed class Corpus
        {
            private readonly HashSet<uint> _objectIds = [];
            private readonly HashSet<uint> _mediaIds = [];
            public readonly Dictionary<uint, List<uint>> EventActionIds = [];
            public readonly Dictionary<uint, ActionTarget> ActionTargets = [];
            public readonly HashSet<uint> DialogueEventIds = [];
            public readonly List<DialogueEventEntry> DialogueEvents = [];
            public readonly List<EventEntry> Events = [];

            public int ObjectIdCount => _objectIds.Count;
            public int MediaIdCount => _mediaIds.Count;
            public bool ObjectExists(uint id) => _objectIds.Contains(id);
            public bool MediaExists(uint id) => _mediaIds.Contains(id);

            public void Add(string sourceKey, BnkFile bank)
            {
                if (bank.DidxChunk != null)
                {
                    foreach (var media in bank.DidxChunk.MediaList)
                        _mediaIds.Add(media.Id);
                }

                if (bank.HircChunk == null)
                    return;

                foreach (var hirc in bank.HircChunk.HircItems)
                {
                    _objectIds.Add(hirc.Id);

                    switch (hirc)
                    {
                        case ICAkEvent akEvent:
                            EventActionIds[hirc.Id] = akEvent.GetActionIds();
                            Events.Add(new EventEntry(sourceKey, hirc.Id));
                            break;
                        case ICAkAction action:
                            ActionTargets[hirc.Id] = new ActionTarget(action.GetChildId(), action.GetActionType());
                            break;
                        case ICAkDialogueEvent dialogueEvent:
                            DialogueEventIds.Add(hirc.Id);
                            DialogueEvents.Add(new DialogueEventEntry(sourceKey, hirc.Id, hirc.GetType().Name, dialogueEvent, ObjectExists, MediaExists));
                            break;
                    }
                }
            }
        }

        internal sealed record ActionTarget(uint TargetId, AkActionType ActionType);

        // An action's IdExt is only a HIRC object reference for the families that act on the actor-mixer
        // hierarchy. The state, switch, game-parameter and trigger families put a game-sync id there
        // instead -- a State Group, Switch Group, RTPC or Trigger -- so looking those up among HIRC
        // object ids answers the wrong question and reports content that is perfectly well-formed as a
        // dangling reference.
        private static bool TargetIsHircObject(AkActionType actionType) => actionType switch
        {
            AkActionType.SetState or AkActionType.UseState_E or AkActionType.UnuseState_E => false,
            AkActionType.SetSwitch => false,
            AkActionType.SetGameParameter or AkActionType.SetGameParameter_O => false,
            AkActionType.ResetGameParameter or AkActionType.ResetGameParameter_O => false,
            AkActionType.Trigger or AkActionType.Trigger_O or AkActionType.Trigger_E or AkActionType.Trigger_E_O => false,
            _ => true
        };

        private sealed record DialogueEventEntry(string SourceKey, uint Id, string ExactTypeName, ICAkDialogueEvent DialogueEvent, Func<uint, bool> ObjectExists, Func<uint, bool> MediaExists);
        private sealed record EventEntry(string SourceKey, uint Id);

        public sealed record AuditResult(
            string Fingerprint, bool ManifestFound, IReadOnlyList<string> PackPaths, IReadOnlyList<string> Failures,
            int ObjectIdCount, IReadOnlyList<DialogueEventResult> DialogueEvents, IReadOnlyList<ActionEventResult> ActionEvents);

        public sealed record DialogueEventResult(
            string SourceKey, uint Id, string ExactTypeName, int ArgumentCount, int FlatNodeCount, int NodesReached,
            int LeavesReached, int LeavesWithZeroTarget, int LeavesWithUnresolvedTarget,
            int LeavesWhoseTargetIsAMediaId, int EarlyTerminatedLeaves, int UnresolvedAtEarlyTerminatedLeaf,
            int MaxDepth, bool Terminates, bool CycleDetected, bool DepthExceeded,
            IReadOnlyList<uint> SampleUnresolvedTargets);

        public sealed record ActionEventResult(
            string SourceKey, uint Id, int ActionsVisited, int TargetsResolved, int TargetsUnresolved, int GameSyncTargets,
            int MaxDepth, bool IsClean, bool CycleDetected, bool DepthExceeded,
            IReadOnlyDictionary<string, int> UnresolvedByActionType);

        private sealed record Metadata(
            string Fingerprint, bool ManifestFound, IReadOnlyList<string> PackPaths,
            int ObjectIdCount, int DialogueEventCount, int ActionEventCount, IReadOnlyList<string> Failures);
    }
}
