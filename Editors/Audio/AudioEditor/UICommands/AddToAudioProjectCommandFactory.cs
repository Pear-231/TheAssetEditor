using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Editors.Audio.AudioEditor.AudioProjectExplorer;
using Shared.Core.Events;

namespace Editors.Audio.AudioEditor.UICommands
{
    public interface IAddToAudioProjectUICommand : IUiCommand
    {
        NodeType NodeType { get; }
        void Execute(DataRow row);
    }

    public class AddToAudioProjectCommandFactory : IUiCommand
    {
        private readonly Dictionary<NodeType, IAddToAudioProjectUICommand> _uiCommands;

        public AddToAudioProjectCommandFactory(IEnumerable<IAddToAudioProjectUICommand> nodeTypes)
        {
            _uiCommands = nodeTypes.ToDictionary(nodeType => nodeType.NodeType);
        }

        public void Execute(NodeType nodeType, DataRow row)
        {
            if (!_uiCommands.TryGetValue(nodeType, out var command))
                throw new InvalidOperationException($"No command registered for {nodeType}.");

            command.Execute(row);
        }
    }
}
