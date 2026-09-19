using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Versions;

namespace Shared.GameFormats.Wwise.Hirc
{
    internal static class ActionParameterReader
    {
        public static byte[] Read(ByteChunk chunk, AkActionType actionType, WwiseVersionDefinition versionDefinition)
        {
            var start = chunk.Index;
            var parameter = versionDefinition.ClassifyAction(actionType);

            if (parameter is AkActionParameter.Active or AkActionParameter.ActiveWithSpecificParameter)
                ReadActive(chunk, versionDefinition, parameter == AkActionParameter.ActiveWithSpecificParameter);
            else if (parameter == AkActionParameter.Mute)
            {
                chunk.Advance(1);
                ReadExceptions(chunk, versionDefinition);
            }
            else if (parameter == AkActionParameter.SetProperty)
            {
                chunk.Advance(1 + 1 + 3 * sizeof(float));
                ReadExceptions(chunk, versionDefinition);
            }
            else if (parameter == AkActionParameter.SetGameParameter)
            {
                chunk.Advance(2 + 1 + 3 * sizeof(float));
                ReadExceptions(chunk, versionDefinition);
            }
            else if (parameter == AkActionParameter.BypassEffects)
            {
                chunk.Advance(2);
                ReadExceptions(chunk, versionDefinition);
            }
            else if (parameter == AkActionParameter.Seek)
            {
                chunk.Advance(1 + 3 * sizeof(float) + 1);
                ReadExceptions(chunk, versionDefinition);
            }

            return chunk.GetBytesFromBuffer(start, chunk.Index - start);
        }

        private static void ReadActive(ByteChunk chunk, WwiseVersionDefinition versionDefinition, bool hasSpecificParameter)
        {
            chunk.Advance(1);
            if (hasSpecificParameter)
                chunk.Advance(1);
            ReadExceptions(chunk, versionDefinition);
        }

        private static void ReadExceptions(ByteChunk chunk, WwiseVersionDefinition versionDefinition)
        {
            var count = versionDefinition.UsesVariableActionExceptionCount
                ? WwiseVariableUInt32Parser.Read(chunk)
                : chunk.ReadUInt32();
            if (count > int.MaxValue / 5 || chunk.BytesLeft < count * 5)
                throw new InvalidDataException($"Wwise action exception list of {count} items exceeds its HIRC object.");
            chunk.Advance(checked((int)count * 5));
        }
    }
}
