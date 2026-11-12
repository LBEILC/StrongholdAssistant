using System.Text.Json;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class SklandCatalogTests
{
    [Fact]
    public async Task CustomOperatorsReachCatalogWithIdentifiersAndEmptyTraits()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory);
            var entries = api.Catalog.GetProperty("operators").EnumerateArray().ToArray();
            Assert.Equal(217, entries.Length);
            Assert.All(entries, e =>
            {
                var skills = e.GetProperty("skills").EnumerateArray().ToArray();
                var modules = e.GetProperty("modules").EnumerateArray().ToArray();
                Assert.NotEmpty(skills);
                Assert.Contains(skills, s => s.GetProperty("id").GetString() == e.GetProperty("defaultSkill").GetString());
                if (modules.Length == 0) Assert.Equal(JsonValueKind.Null, e.GetProperty("defaultModule").ValueKind);
                else Assert.Contains(modules, m => m.GetProperty("id").GetString() == e.GetProperty("defaultModule").GetString());
                Assert.All(skills.Concat(modules), option =>
                    Assert.False(string.IsNullOrWhiteSpace(option.GetProperty("name").GetString())));
            });
            Assert.Equal(7, entries.Count(e => e.GetProperty("modules").GetArrayLength() == 0));
            var estell = entries.Single(e => e.GetProperty("name").GetString() == "艾丝黛尔");
            Assert.Equal(new[] { "攻击力强化·β型", "舍身突击" }, estell.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
            Assert.Equal("荒野地的饯别礼", estell.GetProperty("modules")[0].GetProperty("name").GetString());
            Assert.Equal("skchr_amiya3_2", entries.Single(e => e.GetProperty("name").GetString() == "阿米娅").GetProperty("defaultSkill").GetString());
            var custom = entries.Where(e => e.GetProperty("diy").GetBoolean()).ToArray();
            Assert.Equal(75, custom.Length);
            Assert.All(entries.Take(142), e => Assert.False(e.GetProperty("diy").GetBoolean()));
            Assert.All(custom, e =>
            {
                Assert.Equal(5, e.GetProperty("tier").GetInt32());
                Assert.InRange(e.GetProperty("core").GetArrayLength(), 0, 1);
                Assert.Equal(e.GetProperty("core").GetArrayLength() == 0 ? new[] { "empty" } : [],
                    e.GetProperty("extra").EnumerateArray().Select(x => x.GetString()).ToArray());
                Assert.Equal(0, e.GetProperty("normal").GetArrayLength());
                Assert.Equal(0, e.GetProperty("elite").GetArrayLength());
                Assert.Equal(2, e.GetProperty("equipSlots").GetProperty("normal").GetInt32());
                Assert.Equal(2, e.GetProperty("equipSlots").GetProperty("elite").GetInt32());
            });
            Assert.Equal(43, custom.Count(e => e.GetProperty("extra").GetArrayLength() == 1));
            var chen = custom.Single(e => e.GetProperty("name").GetString() == "赤刃明霄陈");
            Assert.Equal("char_1050_chen3", chen.GetProperty("charId").GetString());
            Assert.Equal("yan", chen.GetProperty("core")[0].GetString());
            Assert.Equal(0, custom.Single(e => e.GetProperty("name").GetString() == "凯尔希·思衡托").GetProperty("core").GetArrayLength());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task EveryCatalogEntryHasUniqueGameIdentifier()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory);
            foreach (var (category, field) in new[] { ("operators", "charId"), ("equipment", "itemId") })
            {
                var entries = api.Catalog.GetProperty(category).EnumerateArray().ToArray();
                var ids = entries.SelectMany(e => new[] { e.GetProperty(field).GetString() }
                    .Concat(e.TryGetProperty("itemAliases", out var aliases)
                        ? aliases.EnumerateArray().Select(a => a.GetString()) : [])).ToArray();
                Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
                Assert.Equal(ids.Length, ids.Distinct().Count());
            }
            var equipment = api.Catalog.GetProperty("equipment").EnumerateArray();
            Assert.Equal("chess_item_6_03_e_a", equipment.Single(e => e.GetProperty("name").GetString() == "变形同构体").GetProperty("itemId").GetString());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
