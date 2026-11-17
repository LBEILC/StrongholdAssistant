using System.Text.Json;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;
namespace StrongholdAssistant.Tests;
public sealed class RosterStoreTests
{
    [Fact]
    public async Task CompleteReadingRemovesOldSupportAndReplacesEditedFields()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        try
        {
            var api = await HostApi.CreateAsync(data, root);
            Assert.Empty(api.ReadRoster().Operators);
            using var patch = JsonDocument.Parse("{\"skin\":null,\"skill\":\"skchr_bison_2\",\"module\":\"\",\"potential\":6}");
            api.SetRosterOperator("拜松", patch.RootElement);
            using var supportPatch = JsonDocument.Parse("{\"potential\":1}");
            api.SetRosterOperator("夜莺", supportPatch.RootElement);
            var reloaded = await HostApi.CreateAsync(data, root);
            Assert.Equal(6, reloaded.ReadRoster().Operators.Single(o => o.Name == "拜松").Potential);
            var card = new RosterCard(new("拜松", "char_325_bison#2", "skcom_def_up[3]", "", 1, DateTimeOffset.UtcNow.ToString("O")), true, 269, 208, 1, 1, "潜能1", 1, null, null);
            var seen = new HashSet<string>();
            var squad = new List<string>();
            reloaded.MergeRoster(new([card, card], 1, 0, []), seen, squad);
            reloaded.CompleteRosterScan("user", seen, squad);
            Assert.Equal(2, reloaded.ReadRoster().Operators.Length);
            Assert.Contains(reloaded.ReadRoster().Operators, o => o.Name == "夜莺");
            reloaded.CompleteRosterScan("end", seen, squad);
            reloaded = await HostApi.CreateAsync(data, root);
            var roster = reloaded.ReadRoster();
            Assert.Single(roster.Operators);
            Assert.Equal("拜松", roster.Operators[0].Name);
            Assert.Null(roster.Operators[0].Edited);
            Assert.Equal("char_325_bison#2", roster.Operators[0].Skin);
            Assert.Equal("skcom_def_up[3]", roster.Operators[0].Skill);
            Assert.Equal("", roster.Operators[0].Module);
            Assert.Equal(1, roster.Operators[0].Potential);
            Assert.Equal(new[] { "拜松" }, roster.CustomSquad);
            await reloaded.ExportBackupAsync(Path.Combine(root, "backup.json"));
            Assert.DoesNotContain("char_325_bison#2", await File.ReadAllTextAsync(Path.Combine(root, "backup.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LegacyEliteAndLevelAreIgnoredAndNotSaved()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "roster.json");
            await File.WriteAllTextAsync(path, """
                {"readAt":null,"operators":[{"name":"拜松","skin":null,"skill":null,"module":"","potential":6,"elite":2,"level":80,"readAt":"2026-11-15T00:00:00Z"}],"customSquad":[]}
                """);
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), root);
            Assert.Equal(6, api.ReadRoster().Operators.Single().Potential);
            using var patch = JsonDocument.Parse("{\"potential\":1}");
            api.SetRosterOperator("拜松", patch.RootElement);
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var op = saved.RootElement.GetProperty("operators")[0];
            Assert.False(op.TryGetProperty("elite", out _));
            Assert.False(op.TryGetProperty("level", out _));
            Assert.Equal(1, op.GetProperty("potential").GetInt32());
        }
        finally { Directory.Delete(root, true); }
    }
}
