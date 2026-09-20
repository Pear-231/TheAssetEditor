using Editors.Audio.Shared.AudioProject.Models;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;

namespace Editors.Audio.Shared.Wwise.Generators.Hirc.V136
{
    public class CAkRanSeqCntrGenerator_V136 : IHircGeneratorService
    {
        public HircItem GenerateHirc(AudioProjectItem audioProjectItem, SoundBank soundBank = null)
        {
            var audioProjectRandomSequenceContainer = audioProjectItem as RandomSequenceContainer;

            var randomSequenceContainerHirc = CreateRandomSequenceContainerHirc(audioProjectRandomSequenceContainer);
            randomSequenceContainerHirc.NodeBaseParams = NodeBaseParamsGenerator_V136.CreateNodeBaseParams(audioProjectRandomSequenceContainer);

            if (audioProjectRandomSequenceContainer.HircSettings.LoopingType == LoopingType.FiniteLooping)
                randomSequenceContainerHirc.LoopCount = (ushort)audioProjectRandomSequenceContainer.HircSettings.NumberOfLoops;
            else if (audioProjectRandomSequenceContainer.HircSettings.LoopingType == LoopingType.InfiniteLooping)
                randomSequenceContainerHirc.LoopCount = 0;
            else if (audioProjectRandomSequenceContainer.HircSettings.LoopingType == LoopingType.Disabled)
                randomSequenceContainerHirc.LoopCount = 1;

            randomSequenceContainerHirc.LoopModMin = 0;
            randomSequenceContainerHirc.LoopModMax = 0;

            if (audioProjectRandomSequenceContainer.HircSettings.TransitionDuration != 1)
                randomSequenceContainerHirc.TransitionTime = (ushort)audioProjectRandomSequenceContainer.HircSettings.TransitionDuration * 1000;
            else
                randomSequenceContainerHirc.TransitionTime = 1 * 1000;

            randomSequenceContainerHirc.TransitionTimeModMin = 0;
            randomSequenceContainerHirc.TransitionTimeModMax = 0;

            if (audioProjectRandomSequenceContainer.HircSettings.RepetitionInterval != 1)
                randomSequenceContainerHirc.AvoidRepeatCount = (ushort)audioProjectRandomSequenceContainer.HircSettings.RepetitionInterval;
            else
                randomSequenceContainerHirc.AvoidRepeatCount = 1;

            var transitionType = audioProjectRandomSequenceContainer.HircSettings.TransitionType;
            if (transitionType == TransitionType.Disabled)
                randomSequenceContainerHirc.TransitionMode = AkTransitionMode.Disabled;
            else if (transitionType == TransitionType.XfadeAmp)
                randomSequenceContainerHirc.TransitionMode = AkTransitionMode.CrossFadeAmp;
            else if (transitionType == TransitionType.XfadePower)
                randomSequenceContainerHirc.TransitionMode = AkTransitionMode.CrossFadePower;
            else if (transitionType == TransitionType.Delay)
                randomSequenceContainerHirc.TransitionMode = AkTransitionMode.Delay;
            else if (transitionType == TransitionType.SampleAccurate)
                randomSequenceContainerHirc.TransitionMode = AkTransitionMode.SampleAccurate;
            else if (transitionType == TransitionType.TriggerRate)
                randomSequenceContainerHirc.TransitionMode = AkTransitionMode.TriggerRate;
            else
                throw new ArgumentOutOfRangeException(nameof(transitionType), transitionType, "Unsupported transition type.");

            if (audioProjectRandomSequenceContainer.HircSettings.ContainerType == ContainerType.Random)
            {
                if (audioProjectRandomSequenceContainer.HircSettings.RandomType == RandomType.Shuffle)
                    randomSequenceContainerHirc.RandomMode = AkRandomMode.Shuffle;
                else
                    randomSequenceContainerHirc.RandomMode = AkRandomMode.Normal;
            }

            if (audioProjectRandomSequenceContainer.HircSettings.ContainerType == ContainerType.Sequence)
                randomSequenceContainerHirc.Mode = AkContainerMode.Sequence;
            else
                randomSequenceContainerHirc.Mode = AkContainerMode.Random;

            var isUsingWeight = 0;

            var resetPlaylistAtEachPlay = 0;
            if (audioProjectRandomSequenceContainer.HircSettings.AlwaysResetPlaylist)
                resetPlaylistAtEachPlay = 1;

            var isRestartBackwards = 0;
            if (audioProjectRandomSequenceContainer.HircSettings.PlaylistEndBehaviour == PlaylistEndBehaviour.PlayInReverseOrder)
                isRestartBackwards = 1;

            var isContinous = 0;
            if (audioProjectRandomSequenceContainer.HircSettings.PlayMode == PlayMode.Continuous)
                isContinous = 1;

            var isGlobal = 1;

            randomSequenceContainerHirc.BitVector = (byte)
            (
                isGlobal << 4 |
                isContinous << 3 |
                isRestartBackwards << 2 |
                resetPlaylistAtEachPlay << 1 |
                isUsingWeight
            );

            var sounds = soundBank.GetSounds(audioProjectRandomSequenceContainer.Children);
            randomSequenceContainerHirc.Children = ChildrenGenerator_V136.CreateChildrenList(sounds);
            randomSequenceContainerHirc.CAkPlayList.Playlist = AkPlaylistItemGenerator_V136.CreateAkPlaylistItem(sounds);

            randomSequenceContainerHirc.UpdateSectionSize();

            return randomSequenceContainerHirc;
        }

        private static CAkRanSeqCntr_V136 CreateRandomSequenceContainerHirc(RandomSequenceContainer audioProjectRandomSequenceContainer)
        {
            return new CAkRanSeqCntr_V136()
            {
                Id = audioProjectRandomSequenceContainer.Id,
                HircType = audioProjectRandomSequenceContainer.HircType,
            };
        }
    }
}
