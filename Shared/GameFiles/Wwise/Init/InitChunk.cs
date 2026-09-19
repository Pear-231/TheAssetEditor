using System.Text;
using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Init
{
    // The INIT chunk: the plug-ins the game registers, by id and by DLL name.
    //
    // Bank version 118 and later only, which is why a V112 init bank correctly has none. The engine
    // implements no plug-ins, so this is read to be reported rather than to be used -- the same
    // position the bus graph takes on effects it cannot reproduce.
    public class InitChunk
    {
        public ChunkHeader ChunkHeader { get; set; } = new ChunkHeader();
        public uint PluginCount { get; set; }
        public List<PluginEntry> Plugins { get; set; } = [];

        public void ReadData(string fileName, ByteChunk chunk)
        {
            ChunkHeader.ReadData(chunk);
            PluginCount = chunk.ReadUInt32();
            for (var pluginIndex = 0; pluginIndex < PluginCount; pluginIndex++)
            {
                var plugin = new PluginEntry();
                plugin.ReadData(chunk);
                Plugins.Add(plugin);
            }
        }

        public class PluginEntry
        {
            // Sixteen bits of plug-in id, twelve of company id and four of type, packed into one
            // word. Kept whole: nothing here needs the parts, and splitting them would invite the
            // reader to pretend it knows what the plug-in does.
            public uint PluginId { get; set; }
            public string DllName { get; set; } = string.Empty;

            public void ReadData(ByteChunk chunk)
            {
                PluginId = chunk.ReadUInt32();
                var stringSize = chunk.ReadUInt32();
                DllName = Encoding.UTF8.GetString(chunk.ReadBytes((int)stringSize)).TrimEnd('\0');
            }
        }
    }
}
