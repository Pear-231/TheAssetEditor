using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Editors.Audio.AudioEditor.Models;
using Editors.Audio.GameSettings.Warhammer3;
using static Editors.Audio.GameSettings.Warhammer3.DialogueEvents;

namespace Editors.Audio.AudioEditor.AudioProjectExplorer.Filters
{
    /// <summary>
    /// Immutable inputs that influence tree visibility.
    /// Add a property here whenever you introduce a new rule.
    /// </summary>
    public sealed record AudioProjectTreeFilterOptions(
        string? SearchQuery = null,
        bool ShowEditedItemsOnly = false,
        AudioProject? AudioProject = null);

    public interface IAudioProjectTreeFilterService
    {
        /// <summary>Applies <paramref name="options"/> to <paramref name="tree"/> in-place.</summary>
        void Apply(ObservableCollection<AudioProjectTreeNode> tree,
                   AudioProjectTreeFilterOptions options);
    }

    /// <remarks>Stateless; register as a singleton in DI.</remarks>
    public sealed class AudioProjectTreeFilterService : IAudioProjectTreeFilterService
    {
        public void Apply(ObservableCollection<AudioProjectTreeNode> tree,
                          AudioProjectTreeFilterOptions opt)
        {
            if (tree is null) throw new ArgumentNullException(nameof(tree));
            if (opt is null) throw new ArgumentNullException(nameof(opt));

            // ---------- 1.  Pre-compute “edited” look-ups ----------
            HashSet<string>? editedActionBanks = null;
            HashSet<string>? editedDialogueBanks = null;
            HashSet<string>? editedStateGroups = null;

            if (opt.ShowEditedItemsOnly && opt.AudioProject is not null)
            {
                editedActionBanks = opt.AudioProject.GetEditedActionEventSoundBanks()
                                                       .Select(b => b.Name).ToHashSet();
                editedDialogueBanks = opt.AudioProject.GetEditedDialogueEventSoundBanks()
                                                       .Select(b => b.Name).ToHashSet();
                editedStateGroups = opt.AudioProject.GetEditedStateGroups()
                                                       .Select(g => g.Name).ToHashSet();
            }

            // ---------- 2.  Build dialogue-preset look-up per sound-bank ----------
            var presetLookup = new Dictionary<string, HashSet<string>>();

            void RegisterPresetBanks(AudioProjectTreeNode node)
            {
                if (node.NodeType == AudioProjectTreeNodeType.DialogueEventSoundBank &&
                    node.PresetFilter.HasValue &&
                    node.PresetFilter.Value != DialogueEventPreset.ShowAll)
                {
                    var preset = node.PresetFilter.Value;
                    var subtype = SoundBanks.GetSoundBankSubtype(node.Name);

                    presetLookup[node.Name] = DialogueEventData
                        .Where(d => d.SoundBank == subtype &&
                                    d.DialogueEventPreset.Contains(preset))
                        .Select(d => d.Name)
                        .ToHashSet();
                }
                foreach (var child in node.Children)
                    RegisterPresetBanks(child);
            }

            foreach (var root in tree)
                RegisterPresetBanks(root);

            // ---------- 3.  Depth-first traversal ----------
            foreach (var root in tree)
                ApplyRecursively(root);

            bool ApplyRecursively(AudioProjectTreeNode node)
            {
                // ---- rule A : text search (matches name) ----
                var matchesSearch =
                    string.IsNullOrWhiteSpace(opt.SearchQuery) ||
                    node.Name.Contains(opt.SearchQuery!, StringComparison.OrdinalIgnoreCase);

                // ---- rule B : edited-only flag ----
                var matchesEdited = !opt.ShowEditedItemsOnly || IsNodeEdited(node);

                // ---- rule C : dialogue-preset ----
                var matchesPreset = true;
                if (node.NodeType == AudioProjectTreeNodeType.DialogueEvent &&
                    node.Parent is not null &&
                    presetLookup.TryGetValue(node.Parent.Name, out var allowedSet))
                {
                    matchesPreset = allowedSet.Contains(node.Name);
                }

                // ---- recurse into children ----
                var childVisible = false;
                foreach (var child in node.Children)
                    childVisible |= ApplyRecursively(child);

                // ---- combine results ----
                var passesNonSearchFilters = matchesEdited && matchesPreset;

                if (!node.Children.Any())                // leaf
                {
                    node.IsVisible = passesNonSearchFilters && matchesSearch;
                }
                else if (!childVisible)                  // empty container
                {
                    node.IsVisible = false;
                }
                else                                    // container with at least one visible child
                {
                    // Container ignores its own *name* wrt text search,
                    // but must still honour edited / preset rules.
                    node.IsVisible = passesNonSearchFilters;
                }

                return node.IsVisible;
            }

            // ---------- helper for “edited” evaluation ----------
            bool IsNodeEdited(AudioProjectTreeNode n) => n.NodeType switch
            {
                // Root containers: visible when *any* edited item of that type exists
                AudioProjectTreeNodeType.ActionEventSoundBanksContainer
                    => editedActionBanks?.Any() == true,
                AudioProjectTreeNodeType.DialogueEventSoundBanksContainer
                    => editedDialogueBanks?.Any() == true,
                AudioProjectTreeNodeType.StateGroupsContainer
                    => editedStateGroups?.Any() == true,

                // Concrete items
                AudioProjectTreeNodeType.ActionEventSoundBank
                    => editedActionBanks?.Contains(n.Name) == true,
                AudioProjectTreeNodeType.DialogueEventSoundBank
                    => editedDialogueBanks?.Contains(n.Name) == true,
                AudioProjectTreeNodeType.StateGroup
                    => editedStateGroups?.Contains(n.Name) == true,
                AudioProjectTreeNodeType.DialogueEvent
                    => n.Parent is not null &&
                       editedDialogueBanks?.Contains(n.Parent.Name) == true,

                _ => true
            };
        }
    }
}
