using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

// 供 tools/check-package.ps1 调用，在发布程序内验证真实的原生识别链。
internal static class PackageRecognitionCheck
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 3) return 2;
        try
        {
            var bytes = await File.ReadAllBytesAsync(args[1]);
            var data = Path.Combine(AppContext.BaseDirectory, "data");
            var store = new JsonDataStore(data);
            var (covenants, _) = await store.LoadCovenantsAsync();
            var bosses = new BossDataService();
            await bosses.LoadAsync(data);
            using var ocr = new MaaOcrEngine(data);
            var strategy = new GarrisonOcrStrategy();
            var analyzer = new BanScreenAnalyzer(() => Task.FromResult<byte[]?>(bytes), ocr, strategy,
                new EnemyRecognizer(ocr, strategy, bosses), await store.LoadOperatorsAsync(),
                covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToArray(),
                covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToArray());
            BanScreenResult? result = null;
            EnemyResult? enemy = null;
            string? failure = null;
            analyzer.AnalysisCompleted += value => result = value;
            analyzer.EnemyDetected += value => enemy = value;
            analyzer.AnalysisFailed += value => failure = value;
            await analyzer.RunAnalysisAsync();
            await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(new { result, enemy, failure }));
            return failure is null && result is not null && enemy is not null ? 0 : 1;
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(new { failure = ex.ToString() }));
            return 1;
        }
    }
}
