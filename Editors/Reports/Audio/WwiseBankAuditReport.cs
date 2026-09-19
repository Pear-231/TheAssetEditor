using Editors.Audio.Shared.Wwise;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;

namespace Editors.Reports.Audio
{
    public class GenerateWwiseBankAuditReportCommand(WwiseBankAuditReport generator) : IAeCommand
    {
        private GameTypeEnum _game;

        public void Configure(GameTypeEnum game) => _game = game;
        public void Execute() => generator.Create(_game);
    }

    public class WwiseBankAuditReport(ApplicationSettingsService settings)
    {
        private readonly ApplicationSettingsService _settings = settings;

        public void Create(GameTypeEnum game)
        {
            var gameDirectory = _settings.GetGamePathForGame(game);
            if (string.IsNullOrWhiteSpace(gameDirectory))
                throw new InvalidOperationException($"No game directory is configured for {game}.");

            var dataDirectory = Directory.Exists(Path.Combine(gameDirectory, "data"))
                ? Path.Combine(gameDirectory, "data")
                : gameDirectory;
            var reportName = game == GameTypeEnum.Warhammer3 ? "wh3" : "attila";
            var result = BnkCorpusAudit.Run(reportName, dataDirectory, DirectoryHelper.ReportsDirectory);
            Console.WriteLine($"{game} Wwise audit: {result.SourceCount} source banks, {result.HircCount} HIRCs, {result.Failures.Count} enumeration failures. Report written to {DirectoryHelper.ReportsDirectory}.");
        }
    }
}
