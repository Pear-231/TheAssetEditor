using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Stmg
{
    // The STMG chunk: the project-wide settings that no per-bank chunk carries -- the voice limits,
    // every state group and its transition times, every switch group and the RTPC that drives it,
    // the initial value and ramping of every game parameter, and the acoustic textures.
    //
    // Only the init bank has one. The engine reads state and switch groups from the HIRC side,
    // where a container names the group it switches on, so nothing here is consumed yet; it is read
    // because the init bank cannot be opened at all until every chunk in it can be.
    public class StmgChunk
    {
        public ChunkHeader ChunkHeader { get; set; } = new ChunkHeader();
        public float VolumeThreshold { get; set; }
        public ushort MaxVoiceLimit { get; set; }
        public ushort MaxDangerousVirtualVoiceLimit { get; set; }
        public List<StateGroup> StateGroups { get; set; } = [];
        public List<SwitchGroup> SwitchGroups { get; set; } = [];
        public List<GameParameter> GameParameters { get; set; } = [];
        public List<AcousticTexture> AcousticTextures { get; set; } = [];

        public void ReadData(string fileName, ByteChunk chunk, uint bankGeneratorVersion)
            => ReadData(fileName, chunk, WwiseVersionResolver.Resolve(bankGeneratorVersion));

        public void ReadData(string fileName, ByteChunk chunk, WwiseVersionDefinition versionDefinition)
        {
            ChunkHeader.ReadData(chunk);

            VolumeThreshold = chunk.ReadSingle();
            MaxVoiceLimit = chunk.ReadUShort();
            if (versionDefinition.HasDangerousVirtualVoiceLimit)
                MaxDangerousVirtualVoiceLimit = chunk.ReadUShort();

            var stateGroupCount = chunk.ReadUInt32();
            for (var stateGroupIndex = 0; stateGroupIndex < stateGroupCount; stateGroupIndex++)
            {
                var stateGroup = new StateGroup();
                stateGroup.ReadData(chunk);
                StateGroups.Add(stateGroup);
            }

            var switchGroupCount = chunk.ReadUInt32();
            for (var switchGroupIndex = 0; switchGroupIndex < switchGroupCount; switchGroupIndex++)
            {
                var switchGroup = new SwitchGroup();
                switchGroup.ReadData(chunk);
                SwitchGroups.Add(switchGroup);
            }

            var gameParameterCount = chunk.ReadUInt32();
            for (var gameParameterIndex = 0; gameParameterIndex < gameParameterCount; gameParameterIndex++)
            {
                var gameParameter = new GameParameter();
                gameParameter.ReadData(chunk);
                GameParameters.Add(gameParameter);
            }

            // The definition selects the current acoustic-texture layout. V112 therefore ends the
            // chunk with its game parameters.
            if (!versionDefinition.HasAcousticTextures)
                return;

            var acousticTextureCount = chunk.ReadUInt32();
            for (var acousticTextureIndex = 0; acousticTextureIndex < acousticTextureCount; acousticTextureIndex++)
            {
                var acousticTexture = new AcousticTexture();
                acousticTexture.ReadData(chunk);
                AcousticTextures.Add(acousticTexture);
            }
        }

        public class StateGroup
        {
            public uint StateGroupId { get; set; }
            public uint DefaultTransitionTime { get; set; }
            public List<StateTransition> Transitions { get; set; } = [];

            public void ReadData(ByteChunk chunk)
            {
                StateGroupId = chunk.ReadUInt32();
                DefaultTransitionTime = chunk.ReadUInt32();
                var transitionCount = chunk.ReadUInt32();
                for (var transitionIndex = 0; transitionIndex < transitionCount; transitionIndex++)
                {
                    var transition = new StateTransition();
                    transition.ReadData(chunk);
                    Transitions.Add(transition);
                }
            }
        }

        public class StateTransition
        {
            public uint StateFrom { get; set; }
            public uint StateTo { get; set; }
            public uint TransitionTime { get; set; }

            public void ReadData(ByteChunk chunk)
            {
                StateFrom = chunk.ReadUInt32();
                StateTo = chunk.ReadUInt32();
                TransitionTime = chunk.ReadUInt32();
            }
        }

        public class SwitchGroup
        {
            public uint SwitchGroupId { get; set; }
            public uint RtpcId { get; set; }
            public byte RtpcType { get; set; }
            public List<WwiseCurvePoint> Points { get; set; } = [];

            public void ReadData(ByteChunk chunk)
            {
                SwitchGroupId = chunk.ReadUInt32();
                RtpcId = chunk.ReadUInt32();
                RtpcType = chunk.ReadByte();
                var pointCount = chunk.ReadUInt32();
                for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
                    Points.Add(new WwiseCurvePoint(chunk.ReadSingle(), chunk.ReadSingle(), chunk.ReadUInt32()));
            }
        }

        // A game parameter's starting value and how it is allowed to move towards a new one.
        public class GameParameter
        {
            public uint RtpcId { get; set; }
            public float DefaultValue { get; set; }
            public uint RampType { get; set; }
            public float RampUp { get; set; }
            public float RampDown { get; set; }
            public byte BuiltInParameter { get; set; }

            public void ReadData(ByteChunk chunk)
            {
                RtpcId = chunk.ReadUInt32();
                DefaultValue = chunk.ReadSingle();
                RampType = chunk.ReadUInt32();
                RampUp = chunk.ReadSingle();
                RampDown = chunk.ReadSingle();
                BuiltInParameter = chunk.ReadByte();
            }
        }

        public class AcousticTexture
        {
            public uint Id { get; set; }
            public float AbsorptionOffset { get; set; }
            public float AbsorptionLow { get; set; }
            public float AbsorptionMidLow { get; set; }
            public float AbsorptionMidHigh { get; set; }
            public float AbsorptionHigh { get; set; }
            public float Scattering { get; set; }

            public void ReadData(ByteChunk chunk)
            {
                Id = chunk.ReadUInt32();
                AbsorptionOffset = chunk.ReadSingle();
                AbsorptionLow = chunk.ReadSingle();
                AbsorptionMidLow = chunk.ReadSingle();
                AbsorptionMidHigh = chunk.ReadSingle();
                AbsorptionHigh = chunk.ReadSingle();
                Scattering = chunk.ReadSingle();
            }
        }
    }
}
