using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text.Json;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class DiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void KeepsTenFailuresAndExportsOnlySanitizedDiagnosticData()
    {
        var store = new DiagnosticsStore(_directory);
        using var image = new Bitmap(2400, 1200);
        using var encoded = new MemoryStream();
        image.Save(encoded, ImageFormat.Png);
        var png = encoded.ToArray();
        for (int i = 0; i < 11; i++)
            store.SaveFailure(png, "未找到核心盟约标题。", "pc", 1.1, [$"OCR (0,0,100,100): 第{i}次"]);
        var shots = Directory.GetFiles(Path.Combine(_directory, "diagnostics"), "*.png");
        Assert.Equal(10, shots.Length);
        Assert.Equal(10, Directory.GetFiles(Path.Combine(_directory, "diagnostics"), "*.json").Length);
        using var saved = new Bitmap(shots[0]);
        Assert.Equal((1920, 960), (saved.Width, saved.Height));
        Directory.CreateDirectory(Path.Combine(_directory, "logs"));
        File.WriteAllText(Path.Combine(_directory, "logs", "host.log"),
            Path.Combine(@"C:\", "Users", "ExampleName", "data") + "\naccess_token=private-token\nCDK=private-code\nAuthorization: Bearer private-auth");
        File.WriteAllText(Path.Combine(_directory, "plaza-token.json"), "private-plaza");
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"cloudSync":{"credential":"private-cloud"},"skland":"private-skland"}""");
        var settings = new SettingsDto("terminal", true, true)
        {
            CloudSync = new(true, "private-cloud", null, null, []),
            MumuSerial = "private-device", ShareIncome = true,
            UpdateSource = "mirrorchyan", MirrorChyanCdk = "private-cdk"
        };
        var path = Path.Combine(_directory, "diagnostics.zip");
        store.Export(path, settings, new("connected", "2400×1200", Source: "pc"),
            new { version = "0.7.2", windows = "Windows", dpi = 144, monitors = new[] { new { width = 2560, height = 1440 } },
                path = Path.Combine(@"C:\", "Users", "ExampleName", "data"), chinesePath = Path.Combine(@"C:\", "Users", "示例 用户", "data") });
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(24, zip.Entries.Count);
        Assert.Contains(zip.Entries, e => e.FullName == "connection.json");
        var text = string.Join('\n', zip.Entries.Where(e => !e.FullName.EndsWith(".png")).Select(e =>
        {
            using var reader = new StreamReader(e.Open());
            return reader.ReadToEnd();
        }));
        Assert.DoesNotContain("ExampleName", text);
        Assert.DoesNotContain("private-", text);
        using var settingsReader = new StreamReader(zip.GetEntry("settings.json")!.Open());
        using var json = JsonDocument.Parse(settingsReader.ReadToEnd());
        Assert.False(json.RootElement.TryGetProperty("cloudSync", out _));
        Assert.False(json.RootElement.TryGetProperty("shareIncome", out _));
        Assert.False(json.RootElement.TryGetProperty("mumuSerial", out _));
        Assert.False(json.RootElement.TryGetProperty("mirrorChyanCdk", out _));
        Assert.Equal("mirrorchyan", json.RootElement.GetProperty("updateSource").GetString());
        Assert.Equal("remember", json.RootElement.GetProperty("startupMode").GetString());
        using var systemReader = new StreamReader(zip.GetEntry("system.json")!.Open());
        using var system = JsonDocument.Parse(systemReader.ReadToEnd());
        Assert.Contains("[用户]", system.RootElement.GetProperty("path").GetString());
        Assert.Equal(Path.Combine(@"C:\", "Users", "[用户]", "data"), system.RootElement.GetProperty("chinesePath").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
