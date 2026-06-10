using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Editors.BmdEditor.Services
{
    internal sealed class BmdLoadDiagnostics
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, int> _unresolvedBuildings = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _failedModels = new(StringComparer.OrdinalIgnoreCase);
        private string _path = string.Empty;

        public void Begin(string bmdPath)
        {
            var outputDirectory = FindResearchMapsDirectory()
                ?? Path.Combine(Environment.CurrentDirectory, "Logs");

            Directory.CreateDirectory(outputDirectory);
            _path = Path.Combine(outputDirectory, "load_log.txt");
            _unresolvedBuildings.Clear();
            _failedModels.Clear();
            File.WriteAllText(_path, $"BMD load started: {DateTime.Now:O}{Environment.NewLine}File: {bmdPath}{Environment.NewLine}");
        }

        private static string? FindResearchMapsDirectory()
        {
            foreach (var startPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(startPath);
                while (directory != null)
                {
                    var candidate = Path.Combine(directory.FullName, "Research", "maps");
                    if (Directory.Exists(candidate))
                        return candidate;
                    directory = directory.Parent;
                }
            }

            return null;
        }

        public void Write(string message)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_path))
                    File.AppendAllText(_path, message + Environment.NewLine);
            }
        }

        public void UnresolvedBuilding(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                key = "<empty>";
            _unresolvedBuildings[key] = _unresolvedBuildings.GetValueOrDefault(key) + 1;
        }

        public void FailedModel(string path, string reason)
        {
            var key = $"{path} :: {reason}";
            _failedModels[key] = _failedModels.GetValueOrDefault(key) + 1;
        }

        public void Complete()
        {
            Write("");
            Write("Unresolved battlefield building keys:");
            foreach (var item in _unresolvedBuildings.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                Write($"  {item.Value,5}  {item.Key}");

            Write("");
            Write("Failed model loads:");
            foreach (var item in _failedModels.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                Write($"  {item.Value,5}  {item.Key}");
        }
    }
}
