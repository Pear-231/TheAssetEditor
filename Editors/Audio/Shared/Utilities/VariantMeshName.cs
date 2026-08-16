using System.IO;

namespace Editors.Audio.Shared.Utilities
{
    public static class VariantMeshName
    {
        public static string Normalise(string variantMeshFilePath)
        {
            if (string.IsNullOrWhiteSpace(variantMeshFilePath))
                return "";

            var variantMeshFileName = Path.GetFileName(variantMeshFilePath.Replace('\\', '/'));
            return Path.GetFileNameWithoutExtension(variantMeshFileName);
        }
    }
}
