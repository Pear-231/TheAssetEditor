namespace Editors.BattleMapEditor.ViewModels
{
    public class BattleMapResourceViewModel
    {
        public string FileName { get; }
        public string PackPath { get; }
        public string Type { get; }
        public bool IsFound { get; }

        public BattleMapResourceViewModel(string fileName, string packPath, string type, bool isFound)
        {
            FileName = fileName;
            PackPath = packPath;
            Type = type;
            IsFound = isFound;
        }
    }
}
