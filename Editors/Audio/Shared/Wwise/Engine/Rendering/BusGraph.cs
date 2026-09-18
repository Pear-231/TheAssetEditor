using Editors.Audio.Shared.Wwise.Engine.Dsp;
using Editors.Audio.Shared.Wwise.Engine.Parameters;
using Editors.Audio.Shared.Wwise.Engine.Telemetry;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Wwise.Engine.Rendering
{
    // An immutable master-mixer topology with mutable block buffers. It is built on the control
    // thread, then handed whole to the renderer. Nodes are sorted deepest-first and by ID, making
    // every sum deterministic even when repositories return bank objects in a different order.
    internal sealed class BusGraph
    {
        private sealed class Node
        {
            public Node(uint id, uint outputBusId, float gain, int maximumBlockFrames)
            {
                Id = id;
                OutputBusId = outputBusId;
                Gain = gain;
                Timeline = new Bus(maximumBlockFrames);
                Immediate = new Bus(maximumBlockFrames);
            }

            public uint Id { get; }
            public uint OutputBusId { get; set; }
            public float Gain { get; }
            public int Depth { get; set; }
            public Bus Timeline { get; }
            public Bus Immediate { get; }
        }

        private readonly Node[] _nodes;
        private readonly Dictionary<uint, Node> _nodesById;
        private readonly GainRamp _busGain = new();

        private BusGraph(Node[] nodes, Dictionary<uint, Node> nodesById)
        {
            _nodes = nodes;
            _nodesById = nodesById;
        }

        public static BusGraph Create(IReadOnlyList<HircItem> hircItems, int maximumBlockFrames, EngineTelemetry telemetry)
        {
            var nodesById = new Dictionary<uint, Node>();
            foreach (var hircItem in hircItems)
            {
                if (hircItem is not ICAkBus bus || nodesById.ContainsKey(hircItem.Id))
                    continue;

                var gainDecibels = ReadNumber(bus.GetProperties(), WwiseProperty.BusVolume)
                    + ReadNumber(bus.GetProperties(), WwiseProperty.OutputBusVolume);
                nodesById.Add(hircItem.Id, new Node(
                    hircItem.Id,
                    bus.GetOutputBusId(),
                    AudioLevel.DecibelsToLinear(gainDecibels),
                    maximumBlockFrames));
                telemetry.CountUnsupportedEffects(bus.GetEffectIds().Count);
                telemetry.CountUnsupportedAuxiliarySends(bus.GetAuxiliaryBusIds().Count);
            }

            // ID zero is the engine's final master input. Missing parents route here explicitly,
            // which keeps incomplete bank sets audible without inventing a hidden hierarchy.
            var master = new Node(0, 0, 1f, maximumBlockFrames);
            nodesById.Add(0, master);
            foreach (var node in nodesById.Values)
            {
                if (node.Id != 0 && (node.OutputBusId == node.Id || !nodesById.ContainsKey(node.OutputBusId)))
                    node.OutputBusId = 0;
            }
            var depthsByNode = nodesById.Values.ToDictionary(node => node, node => FindDepth(node, nodesById));
            foreach (var node in nodesById.Values)
            {
                node.Depth = depthsByNode[node];
                if (node.Depth < 0)
                {
                    node.OutputBusId = 0;
                    node.Depth = 1;
                }
            }

            var nodes = nodesById.Values
                .OrderByDescending(node => node.Depth)
                .ThenBy(node => node.Id)
                .ToArray();
            telemetry.ConfigureBusIds(nodes.Select(node => node.Id));
            return new BusGraph(nodes, nodesById);
        }

        public Bus Route(uint outputBusId, bool isTimelineVoice)
        {
            var node = _nodesById.GetValueOrDefault(outputBusId) ?? _nodesById[0];
            return isTimelineVoice ? node.Timeline : node.Immediate;
        }

        public void Clear(int frameCount)
        {
            foreach (var node in _nodes)
            {
                node.Timeline.Clear(frameCount);
                node.Immediate.Clear(frameCount);
            }
        }

        public void MixTo(Bus destination, GainRamp timelineGain, int frameCount, EngineTelemetry telemetry)
        {
            foreach (var node in _nodes)
            {
                ApplyGain(node.Timeline, node.Gain, frameCount);
                ApplyGain(node.Immediate, node.Gain, frameCount);
                var peak = Math.Max(node.Timeline.MeasurePeak(frameCount), node.Immediate.MeasurePeak(frameCount));
                telemetry.RecordBusPeak(node.Id, peak);
                if (node.Id == 0)
                    continue;

                var parent = _nodesById[node.OutputBusId];
                parent.Timeline.AddFrom(node.Timeline, frameCount);
                parent.Immediate.AddFrom(node.Immediate, frameCount);
            }

            _nodesById[0].Timeline.ApplyGain(timelineGain, frameCount);
            destination.AddFrom(_nodesById[0].Timeline, frameCount);
            destination.AddFrom(_nodesById[0].Immediate, frameCount);
        }

        private void ApplyGain(Bus bus, float gain, int frameCount)
        {
            if (gain == 1f)
                return;
            _busGain.SetImmediately(gain);
            bus.ApplyGain(_busGain, frameCount);
        }

        private static int FindDepth(Node start, Dictionary<uint, Node> nodesById)
        {
            var depth = 0;
            var node = start;
            while (node.Id != 0 && depth < nodesById.Count)
            {
                node = nodesById[node.OutputBusId];
                depth++;
            }
            return node.Id == 0 ? depth : -1;
        }

        private static float ReadNumber(AuthoredProperties properties, WwiseProperty property)
            => properties.TryGetValue(property, out var value) ? value.Number : 0f;
    }
}
