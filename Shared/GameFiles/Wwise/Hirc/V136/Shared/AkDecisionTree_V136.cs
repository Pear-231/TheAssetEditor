using Shared.ByteParsing;
using static Shared.GameFormats.Wwise.Hirc.ICAkDialogueEvent;

namespace Shared.GameFormats.Wwise.Hirc.V136.Shared
{
    public class AkDecisionTree_V136 : IAkDecisionTree
    {
        // Root node of the decision tree in hierarchical form
        public Node_V136 DecisionTree { get; set; } = new Node_V136(); 
        // Flattened list of all nodes in the decision tree in sequential order for read / write
        public List<Node_V136> Nodes { get; set; } = []; 

        public void ReadData(ByteChunk chunk, uint treeDataSize, uint maxTreeDepth)
        {
            var countMax = (ushort)(treeDataSize / new Node_V136().GetSize());

            // Every node is the same width regardless of whether its union field turns out to be a leaf's
            // AudioNodeId or a branch's ChildrenIdx/ChildrenCount, so the whole flat array can be read
            // positionally, with that decision deferred to the tree-build pass below. Deciding it here from
            // read order (or from a guess local to one node) is the defect this replaces: only real tree
            // depth, and consistency with the rest of the tree, can settle it.
            Nodes = new List<Node_V136>(countMax);
            for (var i = 0; i < countMax; i++)
                Nodes.Add(Node_V136.ReadRaw(chunk));

            var claimedByAChild = new bool[countMax];
            if (countMax > 0)
                claimedByAChild[0] = true;
            DecisionTree = ResolveNode(Nodes, 0, 0, maxTreeDepth, countMax, claimedByAChild);
        }

        // Resolves each node's union field by walking the real tree from the root, so "is this node at
        // maximum depth" reflects its actual position rather than its position in read order. Below maximum
        // depth, a candidate branch is only accepted if its children occupy flat-array slots that are in
        // bounds and not already claimed by another node's children: a global partition-consistency check,
        // rather than a guess local to the one node, for the rare case (early-terminated trees; wwiser
        // documents these) where a real leaf sits above maximum depth.
        private static Node_V136 ResolveNode(List<Node_V136> nodes, int index, uint currentDepth, uint maxDepth, ushort countMax, bool[] claimedByAChild)
        {
            if (index >= nodes.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Something went wrong with the number of Decision Tree nodes");

            var node = nodes[index];
            var isMax = currentDepth == maxDepth;

            if (!isMax && TryClaimChildRange(claimedByAChild, node.RawUnion, countMax, out var childrenIdx, out var childrenCount))
            {
                node.ChildrenIdx = childrenIdx;
                node.ChildrenCount = childrenCount;
                node.AudioNodeId = 0;

                var treeNodeChildren = new List<Node_V136>(childrenCount);
                for (var i = 0; i < childrenCount; i++)
                    treeNodeChildren.Add(ResolveNode(nodes, childrenIdx + i, currentDepth + 1, maxDepth, countMax, claimedByAChild));
                node.Nodes = treeNodeChildren;
            }
            else
            {
                node.AudioNodeId = node.RawUnion;
                node.ChildrenIdx = 0;
                node.ChildrenCount = 0;
                node.Nodes = [];
            }

            return node;
        }

        private static bool TryClaimChildRange(bool[] claimedByAChild, uint rawUnion, ushort countMax, out ushort childrenIdx, out ushort childrenCount)
        {
            childrenIdx = (ushort)((rawUnion >> 0) & 0xFFFF);
            childrenCount = (ushort)((rawUnion >> 16) & 0xFFFF);

            if (childrenCount == 0 || childrenIdx + childrenCount > countMax)
                return false;

            for (var i = 0; i < childrenCount; i++)
            {
                if (claimedByAChild[childrenIdx + i])
                    return false;
            }

            for (var i = 0; i < childrenCount; i++)
                claimedByAChild[childrenIdx + i] = true;

            return true;
        }

        public byte[] WriteData()
        {
            using var memStream = new MemoryStream();
            foreach (var node in Nodes)
            {
                memStream.Write(ByteParsers.UInt32.EncodeValue(node.Key, out _), 0, 4);

                var hasChildren = node.Nodes != null && node.Nodes.Count > 0;
                if (!hasChildren)
                    memStream.Write(ByteParsers.UInt32.EncodeValue(node.AudioNodeId, out _), 0, 4);
                else
                {
                    memStream.Write(ByteParsers.UShort.EncodeValue(node.ChildrenIdx, out _), 0, 2);
                    memStream.Write(ByteParsers.UShort.EncodeValue(node.ChildrenCount, out _), 0, 2);
                }

                memStream.Write(ByteParsers.UShort.EncodeValue(node.Weight, out _), 0, 2);
                memStream.Write(ByteParsers.UShort.EncodeValue(node.Probability, out _), 0, 2);
            }
            return memStream.ToArray();
        }

        public uint GetSize()
        {
            var nodeSize = new Node_V136().GetSize();
            return (uint)Nodes.Count * nodeSize;
        }

        public AkDecisionTree_V136 Clone()
        {
            return new AkDecisionTree_V136
            {
                DecisionTree = DecisionTree.Clone(),
                Nodes = Nodes.Select(node => node.CloneWithoutChildren()).ToList()
            };
        }

        public static Node_V136 MergeDecisionTrees(Node_V136 baseDecisionTree, Node_V136 mergingDecisionTree)
        {
            if (baseDecisionTree == null)
                return mergingDecisionTree.Clone();

            if (mergingDecisionTree == null)
                return baseDecisionTree.Clone();

            var targetIsLeaf = baseDecisionTree.Nodes == null || baseDecisionTree.Nodes.Count == 0 || baseDecisionTree.AudioNodeId != 0;
            if (targetIsLeaf)
                return baseDecisionTree.Clone();

            var mergedNodes = new List<Node_V136>(baseDecisionTree.Nodes!);
            var index = baseDecisionTree.Nodes!.ToDictionary(node => node.Key);

            foreach (var mergingChild in mergingDecisionTree.Nodes ?? [])
            {
                if (index.TryGetValue(mergingChild.Key, out var baseChild))
                {
                    var mergedDecisionTree = MergeDecisionTrees(baseChild, mergingChild);
                    if (!ReferenceEquals(baseChild, mergedDecisionTree))
                        mergedNodes[mergedNodes.FindIndex(node => node.Key == baseChild.Key)] = mergedDecisionTree;
                }
                else
                    mergedNodes.Add(mergingChild.Clone());
            }

            mergedNodes = SortNodes(mergedNodes);

            return CreateMergedNode(baseDecisionTree, mergingDecisionTree, mergedNodes);
        }

        private static uint GetKey(Node_V136 baseDecisionTree, Node_V136 mergingDecisionTree)
        {
            if (baseDecisionTree.Key != 0)
                return baseDecisionTree.Key;

            return mergingDecisionTree.Key;
        }

        private static ushort GetWeight(Node_V136 baseDecisionTree, Node_V136 mergingDecisionTree)
        {
            if (baseDecisionTree.Weight != 0)
                return baseDecisionTree.Weight;

            return mergingDecisionTree.Weight;
        }

        private static ushort GetProbability(Node_V136 baseDecisionTree, Node_V136 mergingDecisionTree)
        {
            if (baseDecisionTree.Probability != 0)
                return baseDecisionTree.Probability;

            return mergingDecisionTree.Probability;
        }

        public static List<Node_V136> FlattenDecisionTree(Node_V136 rootNode)
        {
            if (rootNode == null)
                return [];

            var flattenedDecisionTree = new List<Node_V136> { rootNode };
            PrepareAndFlattenChildren(rootNode, flattenedDecisionTree);
            return flattenedDecisionTree;
        }

        private static void PrepareAndFlattenChildren(Node_V136 node, List<Node_V136> flattened)
        {
            var hasChildren = node.Nodes != null && node.Nodes.Count > 0;
            if (!hasChildren)
            {
                node.ChildrenIdx = 0;
                node.ChildrenCount = 0;
                return;
            }

            node.AudioNodeId = 0;
            node.Nodes = SortNodes(node.Nodes!);

            node.ChildrenIdx = (ushort)flattened.Count;
            node.ChildrenCount = (ushort)node.Nodes.Count;

            foreach (var child in node.Nodes)
                flattened.Add(child);

            foreach (var child in node.Nodes)
                PrepareAndFlattenChildren(child, flattened);
        }

        private static List<Node_V136> SortNodes(IEnumerable<Node_V136> nodes)
        {
            return nodes
                .OrderBy(node => node.Key)
                .ToList();
        }

        private static Node_V136 CreateMergedNode(Node_V136 baseDecisionTree, Node_V136 mergingDecisionTree, List<Node_V136> mergedNodes)
        {
            return new Node_V136
            {
                Key = GetKey(baseDecisionTree, mergingDecisionTree),
                AudioNodeId = 0,
                Weight = GetWeight(baseDecisionTree, mergingDecisionTree),
                Probability = GetProbability(baseDecisionTree, mergingDecisionTree),
                Nodes = mergedNodes
            };
        }

        public IAkDecisionNode GetDecisionTree() => DecisionTree;

        public class Node_V136 : IAkDecisionNode
        {
            public uint Key { get; set; }
            public uint AudioNodeId { get; set; }
            public ushort ChildrenIdx { get; set; }
            public ushort ChildrenCount { get; set; }
            public ushort Weight { get; set; }
            public ushort Probability { get; set; }
            public List<Node_V136> Nodes { get; set; } = [];

            // The raw, undecided leaf/branch union field. Only meaningful between ReadRaw and the
            // tree-build pass that resolves AudioNodeId/ChildrenIdx/ChildrenCount from it.
            internal uint RawUnion { get; private set; }

            public static Node_V136 ReadRaw(ByteChunk chunk)
            {
                var node = new Node_V136();
                node.Key = chunk.ReadUInt32();
                node.RawUnion = chunk.ReadUInt32();
                node.Weight = chunk.ReadUShort();
                node.Probability = chunk.ReadUShort();
                return node;
            }

            public uint GetSize()
            {
                // Either ChildrenIdx and ChildrenCount are used or AudioNodeId is used but in either case the
                // same amount of bytes are used so doesn't matter which one is used to calculate the size here
                var idSize = ByteHelper.GetPropertyTypeSize(Key);
                var childrenIdxSize = ByteHelper.GetPropertyTypeSize(ChildrenIdx);
                var childrenCountSize = ByteHelper.GetPropertyTypeSize(ChildrenCount);
                var weightSize = ByteHelper.GetPropertyTypeSize(Weight);
                var probabilitySize = ByteHelper.GetPropertyTypeSize(Probability);
                return idSize + childrenIdxSize + childrenCountSize + weightSize + probabilitySize;
            }

            public Node_V136 Clone()
            {
                var clonedNode = CloneWithoutChildren();
                clonedNode.Nodes = (Nodes != null) ? Nodes.Select(child => child.Clone()).ToList() : [];
                return clonedNode;
            }

            public Node_V136 CloneWithoutChildren()
            {
                return new Node_V136
                {
                    Key = Key,
                    AudioNodeId = AudioNodeId,
                    ChildrenIdx = ChildrenIdx,
                    ChildrenCount = ChildrenCount,
                    Weight = Weight,
                    Probability = Probability,
                    Nodes = []
                };
            }

            public uint GetKey() => Key;
            public uint GetAudioNodeId() => AudioNodeId;
            public int GetChildrenCount() => Nodes.Count;
            public IAkDecisionNode GetChildAtIndex(int index) => Nodes[index];
        }
    }
}
