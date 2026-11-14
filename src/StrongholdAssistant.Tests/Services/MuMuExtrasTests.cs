using System.Text;
using System.Text.Json.Nodes;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

public class MuMuExtrasTests
{
    private static EmulatorDevice Device => new("MuMu", @"C:\Users\你\MuMu\shell\adb.exe",
        "127.0.0.1:16416", "{}") { DisplayId = 3, GamePackage = "com.hypergryph.arknights" };

    [Theory]
    [InlineData("com.hypergryph.arknights")]
    [InlineData("com.hypergryph.arknights.bilibili")]
    public void GameWindowRetainsPackage(string package)
    {
        var dump = $"Window #0 Window{{111 u0 {package}/.Main}}:\n mDisplayId=3 mHasSurface=true";
        Assert.Equal(3, EmulatorDisplay.FindGameDisplay(dump, out var found));
        Assert.Equal(package, found);
        var device = EmulatorDisplay.Inspect(Device, (_, _) => (0, Encoding.UTF8.GetBytes(dump), ""));
        Assert.Equal(package, device.GamePackage);
    }

    [Fact]
    public void MergesExistingConfigWithoutLosingFields()
    {
        var device = Device with { Config = """
            {"other":42,"extras":{"other":{"enable":true},"mumu":{"path":"D:\\MuMu","index":7,"lib":"custom.dll","custom":9}}}
            """ };
        var settings = MuMuExtras.Resolve(device, _ => false)!;
        Assert.Equal(7, settings.Index);
        Assert.Equal("custom.dll", settings.Library);
        var config = JsonNode.Parse(settings.Config)!;
        Assert.Equal(42, config["other"]!.GetValue<int>());
        Assert.True(config["extras"]!["other"]!["enable"]!.GetValue<bool>());
        var mumu = config["extras"]!["mumu"]!;
        Assert.Equal(9, mumu["custom"]!.GetValue<int>());
        Assert.True(mumu["enable"]!.GetValue<bool>());
        Assert.Equal(device.GamePackage, mumu["app_package"]!.GetValue<string>());
        Assert.Equal(0, mumu["app_cloned_index"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 7)]
    public void ResolvesNxDirectoryAndInstance(bool manager, int index)
    {
        var root = @"C:\Users\你\MuMu";
        var library = Path.Combine(root, "nx_main", "sdk", "external_renderer_ipc.dll");
        var settings = MuMuExtras.Resolve(Device, path => path == library
            || (manager && path == Path.Combine(root, "MuMuManager.exe")),
            _ => """{"7":{"index":7,"adb_port":16416}}""")!;
        Assert.Equal(root, settings.Path);
        Assert.Equal(index, settings.Index);
        Assert.Equal(library, settings.Library);
        Assert.Null(MuMuExtras.Resolve(Device, _ => false));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void InvalidSdkDisplaySkipsControllerAndUsesAdb(int display)
    {
        var logs = new List<string>();
        var capture = new EmulatorCapture(logs.Add);
        var created = false;
        var settings = new MuMuExtrasSettings("unused", 0, "unused", "{}");
        byte[]? Extras()
        {
            MuMuExtras.Prepare(Device, settings, (_, package) =>
            {
                Assert.Equal(Device.GamePackage, package);
                return display;
            });
            created = true;
            return [1];
        }
        for (var i = 0; i < 2; i++)
            Assert.NotNull(capture.Capture(Device, (_, args) => args[^1] == "display"
                ? (0, [], "") : (0, [1], ""), bytes => bytes.Length > 0, out _, Extras));
        Assert.False(created);
        Assert.Single(logs.Where(line => line.Contains("MuMu extras")));
        Assert.Contains($"nemu_get_display_id={display}", logs[0]);
    }

    [Theory]
    [InlineData("MuMu extras")]
    [InlineData("ADB SurfaceFlinger display")]
    [InlineData("ADB logical display")]
    public void AttemptsExtrasThenPhysicalThenLogicalAndRemembersSuccess(string success)
    {
        var calls = new List<string>();
        var capture = new EmulatorCapture(_ => { });
        byte[]? Extras()
        {
            calls.Add("MuMu extras");
            return success == "MuMu extras" ? [1] : throw new IOException("failed");
        }
        (int, byte[], string) Adb(EmulatorDevice _, string[] args)
        {
            if (args[^1] == "display") return (0, Encoding.UTF8.GetBytes(
                "mDisplayId=3\n mBaseDisplayInfo=DisplayInfo{uniqueId \"local:100\"}"), "");
            var method = args[^1] == "100" ? "ADB SurfaceFlinger display" : "ADB logical display";
            calls.Add(method);
            return method == success ? (0, [1], "") : (1, [], "failed");
        }
        Assert.NotNull(capture.Capture(Device, Adb, bytes => bytes.Length > 0, out var selected, Extras));
        Assert.Equal(success, selected);
        var order = new[] { "MuMu extras", "ADB SurfaceFlinger display", "ADB logical display" };
        Assert.Equal(order.Take(Array.IndexOf(order, success) + 1), calls);
        calls.Clear();
        Assert.NotNull(capture.Capture(Device, Adb, bytes => bytes.Length > 0, out _, Extras));
        Assert.Equal(new[] { success }, calls);
    }
}
