using Shared.ByteParsing;
using static Shared.GameFormats.Wwise.Hirc.ICAkDialogueEvent;

namespace Shared.GameFormats.Wwise.Hirc.V112.Shared
{
    public class AkDecisionTree_V112 : IAkDecisionTree
    {
        public Node_V112 DecisionTree { get; set; } = new Node_V112();
        public List<Node_V112> FlattenedDecisionTree { get; set; } = []; 

        public void ReadData(ByteChunk chunk, uint uTreeDataSize, uint maxTreeDepth)
        {
            var countMax = (ushort)(uTreeDataSize / new Node_V112().GetSize());

            // Every node is the same width regardless of whether its union field turns out to be a leaf's
            // AudioNodeId or a branch's ChildrenIdx/ChildrenCount, so the whole flat array can be read
            // positionally, with that decision deferred to the tree-build pass below. Deciding it here from
            // read order (or from a guess local to one node) is the defect this replaces: only real tree
            // depth, and consistency with the rest of the tree, can settle it.
            FlattenedDecisionTree = new List<Node_V112>(countMax);
            for (var i = 0; i < countMax; i++)
                FlattenedDecisionTree.Add(Node_V112.ReadRaw(chunk));

            var claimedByAChild = new bool[countMax];
            if (countMax > 0)
                claimedByAChild[0] = true;
            DecisionTree = ResolveNode(FlattenedDecisionTree, 0, 0, maxTreeDepth, countMax, claimedByAChild);
        }

        // Resolves each node's union field by walking the real tree from the root, so "is this node at
        // maximum depth" reflects its actual position rather than its position in read order. Below maximum
        // depth, a candidate branch is only accepted if its children occupy flat-array slots that are in
        // bounds and not already claimed by another node's children: a global partition-consistency check,
        // rather than a guess local to the one node, for the rare case (early-terminated trees; wwiser
        // documents these) where a real leaf sits above maximum depth.
        private static Node_V112 ResolveNode(List<Node_V112> nodes, int index, uint currentDepth, uint maxDepth, ushort countMax, bool[] claimedByAChild)
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

                var treeNodeChildren = new List<Node_V112>(childrenCount);
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
            foreach (var node in FlattenedDecisionTree)
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

        public IAkDecisionNode GetDecisionTree() => DecisionTree;

        public class Node_V112 : IAkDecisionNode
        {
            public uint Key { get; set; }
            public uint AudioNodeId { get; set; }
            public ushort ChildrenIdx { get; set; }
            public ushort ChildrenCount { get; set; }
            public ushort Weight { get; set; }
            public ushort Probability { get; set; }
            public List<Node_V112> Nodes { get; set; } = [];

            // The raw, undecided leaf/branch union field. Only meaningful between ReadRaw and the
            // tree-build pass that resolves AudioNodeId/ChildrenIdx/ChildrenCount from it.
            internal uint RawUnion { get; private set; }

            public static Node_V112 ReadRaw(ByteChunk chunk)
            {
                var node = new Node_V112();
                node.Key = chunk.ReadUInt32();
                node.RawUnion = chunk.ReadUInt32();
                node.Weight = chunk.ReadUShort();
                node.Probability = chunk.ReadUShort();
                return node;
            }

            public uint GetSize()
            {
                // Either ChildrenIdx and ChildrenCount are used or AudioNodeId is used but in either case the same amount of bytes are used so doesn't matter which one is used to calculate the size here
                var idSize = ByteHelper.GetPropertyTypeSize(Key);
                var childrenIdxSize = ByteHelper.GetPropertyTypeSize(ChildrenIdx);
                var childrenCountSize = ByteHelper.GetPropertyTypeSize(ChildrenCount);
                var weightSize = ByteHelper.GetPropertyTypeSize(Weight);
                var probabilitySize = ByteHelper.GetPropertyTypeSize(Probability);
                return idSize + childrenIdxSize + childrenCountSize + weightSize + probabilitySize;
            }

            public uint GetKey() => Key;
            public uint GetAudioNodeId() => AudioNodeId;
            public int GetChildrenCount() => Nodes.Count;
            public IAkDecisionNode GetChildAtIndex(int index) => Nodes[index];
        }
    }
}
