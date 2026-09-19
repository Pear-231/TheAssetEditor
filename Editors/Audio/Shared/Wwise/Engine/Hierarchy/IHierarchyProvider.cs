using Editors.Audio.Shared.Wwise.Engine.Media;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Wwise.Engine.Hierarchy
{
    // The engine's view of the loaded banks: nodes by id, media by source id, and names for both.
    //
    // Implemented by an adapter over the audio repository, which is what keeps the engine from
    // reading pack files itself while still owning the hierarchy — the walk, the container rules
    // and the media are the engine's, the file access is not.
    //
    // Everything here is called on the control thread, at warm time. The audio thread holds
    // references it was handed and looks nothing up.
    public interface IHierarchyProvider
    {
        HircItem FindEvent(string eventName);

        // Scoped to the bank the reference came from, because the same id can be defined in more
        // than one bank and a reference means the one it was written next to.
        HircItem FindNode(uint nodeId, string referringBnkFilePath);

        string GetName(uint id);

        // Decoded and resident, or null when the bank names a source that is not there.
        SourceMedia FindMedia(uint sourceId, string referringBnkFilePath);

        // Master-mixer HIRCs are global bank objects rather than descendants of an event root.
        IReadOnlyList<HircItem> GetMasterMixerNodes() => [];
    }
}
