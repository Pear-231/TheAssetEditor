using static Editors.Audio.AudioEditor.Settings.Settings;

namespace Editors.Audio.AudioEditor.Models
{
    public class AudioSettings
    {
        public PlaylistType PlaylistType { get; set; }
        public bool EnableRepetitionInterval { get; set; }
        public uint RepetitionInterval { get; set; }
        public EndBehaviour EndBehaviour { get; set; }
        public bool AlwaysResetPlaylist { get; set; }
        public PlaylistMode PlaylistMode { get; set; }
        public LoopingType LoopingType { get; set; }
        public uint NumberOfLoops { get; set; }
        public TransitionType TransitionType { get; set; }
        public decimal TransitionDuration { get; set; }

        public static AudioSettings CreateSoundSettings(AudioSettings audioSettingsStore)
        {
            return new AudioSettings
            {
                LoopingType = audioSettingsStore.LoopingType,
                NumberOfLoops = audioSettingsStore.NumberOfLoops,
            };
        }

        public static AudioSettings CreateRandomSequenceContainerSettings(AudioSettings audioSettingsStore)
        {
            return new AudioSettings
            {
                PlaylistType = audioSettingsStore.PlaylistType,
                EnableRepetitionInterval = audioSettingsStore.EnableRepetitionInterval,
                RepetitionInterval = audioSettingsStore.RepetitionInterval,
                EndBehaviour = audioSettingsStore.EndBehaviour,
                AlwaysResetPlaylist = audioSettingsStore.AlwaysResetPlaylist,
                PlaylistMode = audioSettingsStore.PlaylistMode,
                TransitionType = audioSettingsStore.TransitionType,
                TransitionDuration = audioSettingsStore.TransitionDuration
            };
        }
    }
}
