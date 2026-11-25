using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using Microsoft.ML.OnnxRuntime.Tensors;
using StrongholdAssistant.Host;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests;

public sealed class ShopAdviceTests(ITestOutputHelper output)
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private sealed record Example(MatchState State, ShopContext Context, Dictionary<string, string> Hashes, int Top);

    [Fact]
    public void MatchesPythonTensorsAndTopAction()
    {
        var path = Environment.GetEnvironmentVariable("SHOP_PARITY_FIXTURE")
            ?? Path.Combine(AppContext.BaseDirectory, "TestData", "shop-policy.json");
        var cases = JsonSerializer.Deserialize<Example[]>(File.ReadAllText(path), HostApi.JsonOptions)!;
        using var policy = new ShopPolicy(Path.Combine(Data, "models", "garrison-sp3"));
        var times = new List<double>();
        foreach (var example in cases)
        {
            var obs = policy.Encoder.Encode(example.State, example.Context);
            Assert.True(obs.Available, string.Join(",", obs.Missing));
            Assert.Equal(example.Hashes.Keys.Order(), obs.Inputs.Select(t => t.Name).Order());
            foreach (var t in obs.Inputs)
            {
                byte[] bytes = t.Value switch
                {
                    Tensor<float> floats => MemoryMarshal.AsBytes(floats.ToArray().AsSpan()).ToArray(),
                    Tensor<long> longs => MemoryMarshal.AsBytes(longs.ToArray().AsSpan()).ToArray(),
                    _ => MemoryMarshal.AsBytes(t.AsTensor<bool>().ToArray().AsSpan()).ToArray()
                };
                Assert.True(Convert.ToHexString(SHA256.HashData(bytes)).Equals(example.Hashes[t.Name], StringComparison.OrdinalIgnoreCase),
                    $"规程 {example.State.ModeId.Value} 回合 {example.State.Round.Value} 张量 {t.Name}");
            }
            var start = Stopwatch.GetTimestamp();
            var logits = policy.Logits(obs);
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Assert.Equal(example.Top, Array.IndexOf(logits, logits.Max()));
            Assert.Contains("hand", obs.Missing);
        }
        var sorted = times.Skip(1).Order().ToArray();
        output.WriteLine($"{cases.Length} 帧逐项一致，首选一致。去首帧 C# ORT 中位 {sorted[sorted.Length / 2]:F2} ms，P95 {sorted[(int)Math.Ceiling(sorted.Length * .95) - 1]:F2} ms。");
        var first = cases[0];
        var unavailable = policy.Predict(1, first.State with { Units = MatchFact<MatchUnit[]?>.Unknown }, first.Context);
        Assert.Equal("unavailable", unavailable.Status);
        Assert.Contains("units", unavailable.MissingInputs);
        Assert.Null(unavailable.Action);
    }

    [Fact]
    public async Task OnlyEnabledStableSoloShopPublishesAndLeavingClears()
    {
        var dir = Directory.CreateTempSubdirectory();
        var api = await HostApi.CreateAsync(Data, dir.FullName);
        try
        {
            Assert.False(api.Settings.ShopAdvice);
            List<ShopAdvice> messages = [];
            api.Changed += (name, value) => { if (name == "shopAdvice") messages.Add((ShopAdvice)value); };
            void Mode(string id) => api.ApplyRecognizedMatch(api.Match with { Mode = new(id, id) }, []);
            var cards = new[] { "维式重锤", "杰克", "卡达", "烈夏", "慕斯", "霜叶" }.Select((s, i) =>
                new ShopItem { SlotIndex = i, Id = $"{i}:{s}", Name = s, Price = 2 }).ToArray();
            async Task Frame(bool rest = true, bool visible = true)
            {
                await api.ObserveBoardTurn([], DateTimeOffset.UtcNow, new EconomyReading(4, 8, 4, 4, rest) { RefreshCost = 1, Frozen = false }, true, visible, cards);
                await api.UpdateShopAdviceAsync();
            }
            Mode("mode_single_12");
            await Frame(); await Frame();
            Assert.Empty(messages);
            api.SaveSettings(api.Settings with { ShopAdvice = true });
            Assert.True(api.SettingsStore.LoadSettings().ShopAdvice);
            Mode("mode_multi_hard");
            await Frame();
            Assert.Empty(messages);
            Mode("mode_single_12");
            await Frame(false);
            await Frame();
            Assert.Empty(messages);
            await Frame();
            Assert.Contains("targetCovenant", Assert.Single(messages).MissingInputs);
            await Frame();
            Assert.Single(messages);
            await Frame(visible: false);
            Assert.Equal(2, messages.Count);
            Assert.True(messages[1].StateRevision > messages[0].StateRevision);
            Assert.Null(messages[1].Action);
            await Frame(); await Frame();
            api.CoverMatchHud();
            Assert.Empty(messages[^1].MissingInputs);
            await Frame(); await Frame();
            api.SaveSettings(api.Settings with { ShopAdvice = false });
            Assert.Null(messages[^1].Action);
            int count = messages.Count;
            await Frame();
            Assert.Equal(count, messages.Count);
        }
        finally { api.CloseShopAdvice(); dir.Delete(true); }
    }
}

