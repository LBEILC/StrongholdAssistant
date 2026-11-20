// 2026-11-15：从 CarlosDJY d3dc2f9 移植计算核心与测试，适配本项目输入和数据，保留原声明。
// Modified for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Text.Json;
using ASPAssistant.Core.Models;
namespace ASPAssistant.Core.Placement;
public sealed class JsonDataStore(string _dataDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public async Task<List<Operator>> LoadOperatorsAsync()
    {
        var source = await new ASPAssistant.Core.Data.JsonDataStore(_dataDirectory).LoadOperatorsAsync();
        var operators = JsonSerializer.Deserialize<List<Operator>>(JsonSerializer.Serialize(source), JsonOptions)!;
        await ApplyOperatorProfilesAsync(operators);
        return operators;
    }
    public Task<(List<CovenantInfo>, Dictionary<string, string>)> LoadCovenantsAsync() => new ASPAssistant.Core.Data.JsonDataStore(_dataDirectory).LoadCovenantsAsync();
    private async Task ApplyOperatorProfilesAsync(List<Operator> operators)
    {
        var path = Path.Combine(_dataDirectory, "operator_profiles.json");
        if (!File.Exists(path))
            return;

        var json = await File.ReadAllTextAsync(path);
        var wrapper = JsonSerializer.Deserialize<OperatorProfileDataFile>(json, JsonOptions);
        if (wrapper?.Operators is not { Count: > 0 } profiles)
            return;

        var deploymentPath = Path.Combine(_dataDirectory, "operator_deployment.json");
        if (File.Exists(deploymentPath))
        {
            var data = JsonSerializer.Deserialize<DeploymentFile>(await File.ReadAllTextAsync(deploymentPath), JsonOptions)!;
            foreach (var (name, deployment) in data.Entries)
                if (profiles.TryGetValue(name, out var profile)) profile.Deployment = deployment;
        }
        var calibrationPath = Path.Combine(_dataDirectory, "operator_output_calibration.json");
        if (File.Exists(calibrationPath))
        {
            var data = JsonSerializer.Deserialize<CalibrationFile>(await File.ReadAllTextAsync(calibrationPath), JsonOptions)!;
            foreach (var (name, forms) in data.Entries)
                if (profiles.TryGetValue(name, out var profile)) profile.Calibration = forms;
        }
        var ranges = wrapper.Ranges.ToDictionary(p => p.Key,
            p => new AttackRange(p.Key, p.Value.Where(c => c.Length == 2).Select(c => (c[0], c[1]))));
        foreach (var profile in profiles.Values)
        {
            foreach (var r in new[] { profile.NormalRange, profile.EliteRange }
                .Concat(profile.Skills.Values.SelectMany(s => new[] { s.Normal.Range, s.Elite.Range })))
            {
                if (r is null) continue;
                r.BaseRange = r.Base is { } b && ranges.TryGetValue(b, out var br) ? br : null;
                r.SkillRange = r.Skill is { } k && ranges.TryGetValue(k, out var sr) ? sr : null;
            }
            foreach (var st in new[] { profile.NormalStats, profile.EliteStats })
                if (st?.Summon is { Range: { } sid } summon && ranges.TryGetValue(sid, out var shape)) summon.RangeShape = shape;
        }

        foreach (var op in operators)
        {
            if (!profiles.TryGetValue(op.Name, out var profile)) continue;
            op.Profile = profile;
            op.Normal.Skill = profile.NormalSkill;
            op.Elite.Skill = profile.EliteSkill ?? profile.NormalSkill;
        }
    }

    private sealed record CalibrationFile(Dictionary<string, Dictionary<string, OutputCalibration>> Entries);
    private sealed record DeploymentFile(Dictionary<string, ProfileChoices<DeployStats>> Entries);

    private class OperatorProfileDataFile
    {
        public Dictionary<string, OperatorProfile> Operators { get; set; } = [];
        public Dictionary<string, int[][]> Ranges { get; set; } = [];
    }

}
