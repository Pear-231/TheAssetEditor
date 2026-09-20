using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc.V136.Shared
{
    public class AkPluginParam_V136
    {
        public List<byte> ParamBlock { get; set; } = [];
        //used for default case, "gap" of bytes

        public static AkPluginParam_V136 CreateFromBytes(ByteChunk chunk, uint plugin_id, uint uSize)
        {
            switch (plugin_id)
            {
            // Only SrcSilence is used for WH3... maybe...
            // Actually the default case would take care of any of them, so ehhh
                //case 0x00640002: return CAkFxSrcSineParams.CreateFromBytes(chunk, uSize);
                case 0x00650002:
                {
                    var silenceParams = new CAkFxSrcSilenceParams_V136();
                    silenceParams.ReadData(chunk);
                    return silenceParams;
                }
                //case 0x00660002: return CAkToneGenParams.CreateFromBytes(chunk, uSize);
                //case 0x00690003: return CAkParameterEQFXParams.CreateFromBytes(chunk, uSize);
                //case 0x006A0003: return CAkDelayFXParams.CreateFromBytes(chunk, uSize);
                //case 0x006E0003: return CAkPeakLimiterFXParams.CreateFromBytes(chunk, uSize);
                //case 0x00730003: return CAkFDNReverbFXParams.CreateFromBytes(chunk, uSize);
                //case 0x00760003: return CAkRoomVerbFXParams.CreateFromBytes(chunk, uSize);
                //case 0x007D0003: return CAkFlangerFXParams.CreateFromBytes(chunk, uSize);
                //case 0x007E0003: return CAkGuitarDistortionFXParams.CreateFromBytes(chunk, uSize);
                //case 0x007F0003: return CAkConvolutionReverbFXParams.CreateFromBytes(chunk, uSize);
                //case 0x00810003: return CAkMeterFXParams.CreateFromBytes(chunk, uSize);
                //case 0x00870003: return CAkStereoDelayFXParams.CreateFromBytes(chunk, uSize);
                //case 0x008B0003: return CAkGainFXParams.CreateFromBytes(chunk, uSize);
                //case 0x00940002: return CAkSynthOneParams.CreateFromBytes(chunk, uSize);
                //case 0x00C80002: return CAkFxSrcAudioInputParams.CreateFromBytes(chunk, uSize);
                //case 0x00041033: return iZTrashDelayFXParams.CreateFromBytes(chunk, uSize);
               default:
                    // Default "gap"
                    var akPluginParam_V136 = new AkPluginParam_V136();
                    akPluginParam_V136.ReadData(chunk, uSize);
                    return akPluginParam_V136;
            }
        }

        public void ReadData(ByteChunk chunk, uint uSize)
        {
            ParamBlock = new List<byte>((int)uSize);
            for (var i = 0; i < uSize; i++)
                ParamBlock.Add(chunk.ReadByte());
        }

        public class CAkFxSrcSilenceParams_V136 : AkPluginParam_V136
        {
            public float Duration { get; set; }
            public float RandomisedLengthMinus { get; set; }
            public float RandomisedLengthPlus { get; set; }

            public void ReadData(ByteChunk chunk)
            {
                Duration = chunk.ReadSingle();
                RandomisedLengthMinus = chunk.ReadSingle();
                RandomisedLengthPlus = chunk.ReadSingle();
            }
        }
    }
}
