using Editors.Audio.Shared.AudioProject.Models;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Editors.Audio.Shared.Wwise.Generators.Hirc.V136
{
    public class NodeBaseParamsGenerator_V136
    {
        public static NodeBaseParams_V136 CreateNodeBaseParams(Sound audioProjectSound)
        {
            var soundIsTarget = audioProjectSound.HircSettings != null;

            var nodeBaseParams = new NodeBaseParams_V136
            {
                NodeInitialFxParams = new NodeInitialFxParams_V136()
                {
                    IsOverrideParentFx = 0,
                    NumFx = 0,
                },
                OverrideAttachmentParams = 0,
                OverrideBusId = soundIsTarget ? audioProjectSound.OverrideBusId : 0,
                DirectParentId = audioProjectSound.DirectParentId,
                BitVector = 0,
                NodeInitialParams = new NodeInitialParams_V136()
            };

            if (soundIsTarget && audioProjectSound.HircSettings.LoopingType == LoopingType.FiniteLooping)
            {
                nodeBaseParams.NodeInitialParams.AkPropBundle0 = new AkPropBundle_V136()
                {
                    PropsList = new List<AkPropBundle_V136.PropBundleInstance_V136>
                    {
                        new AkPropBundle_V136.PropBundleInstance_V136
                        {
                            Id = AkPropId.Loop,
                            Value = audioProjectSound.HircSettings.NumberOfLoops
                        }
                    }
                };
            }
            else if (soundIsTarget && audioProjectSound.HircSettings.LoopingType == LoopingType.InfiniteLooping)
            {
                nodeBaseParams.NodeInitialParams.AkPropBundle0 = new AkPropBundle_V136()
                {
                    PropsList = new List<AkPropBundle_V136.PropBundleInstance_V136>
                    {
                        new AkPropBundle_V136.PropBundleInstance_V136
                        {
                            Id = AkPropId.Loop,
                            Value = 0
                        }
                    }
                };
            }
            else
                nodeBaseParams.NodeInitialParams.AkPropBundle0 = new AkPropBundle_V136() { PropsList = new List<AkPropBundle_V136.PropBundleInstance_V136>() };

            nodeBaseParams.NodeInitialParams.AkPropBundle1 = new AkPropBundleMinMax_V136() { PropsList = new List<AkPropBundleMinMax_V136.AkPropBundleInstance_V136>() };
            nodeBaseParams.PositioningParams = new PositioningParams_V136()
            {
                BitsPositioning = 0x00
            };
            nodeBaseParams.AuxParams = new AuxParams_V136()
            {
                BitVector = 0,
                ReflectionsAuxBus = 0
            };
            nodeBaseParams.AdvSettingsParams = new AdvSettingsParams_V136()
            {
                BitVector = 0x00,
                VirtualQueueBehavior = 0x01,
                MaxNumInstance = 0,
                BelowThresholdBehavior = 0,
                BitVector2 = 0x00,
                EnableLoudnessNormalisation = true
            };
            nodeBaseParams.StateChunk = new StateChunk_V136();
            nodeBaseParams.InitialRtpc = new InitialRtpc_V136();
            return nodeBaseParams;
        }

        public static NodeBaseParams_V136 CreateNodeBaseParams(RandomSequenceContainer audioProjectRandomSequenceContainer)
        {
            var nodeBaseParams = new NodeBaseParams_V136
            {
                NodeInitialFxParams = new NodeInitialFxParams_V136()
                {
                    IsOverrideParentFx = 0,
                    NumFx = 0,
                },
                OverrideAttachmentParams = 0,
                OverrideBusId = audioProjectRandomSequenceContainer.OverrideBusId,
                DirectParentId = audioProjectRandomSequenceContainer.DirectParentId,
                BitVector = 0,
                NodeInitialParams = new NodeInitialParams_V136()
                {
                    AkPropBundle0 = new AkPropBundle_V136() { PropsList = new List<AkPropBundle_V136.PropBundleInstance_V136>() },
                    AkPropBundle1 = new AkPropBundleMinMax_V136() { PropsList = new List<AkPropBundleMinMax_V136.AkPropBundleInstance_V136>() }
                },
                PositioningParams = new PositioningParams_V136()
                {
                    BitsPositioning = 0x00,
                },
                AuxParams = new AuxParams_V136()
                {
                    BitVector = 0,
                    ReflectionsAuxBus = 0
                },
                AdvSettingsParams = new AdvSettingsParams_V136()
                {
                    BitVector = 0x00,
                    VirtualQueueBehavior = 0x01,
                    MaxNumInstance = 0,
                    BelowThresholdBehavior = 0,
                    BitVector2 = 0x00,
                    EnableLoudnessNormalisation = true
                },
                StateChunk = new StateChunk_V136(),
                InitialRtpc = new InitialRtpc_V136()
            };
            return nodeBaseParams;
        }
    }
}
