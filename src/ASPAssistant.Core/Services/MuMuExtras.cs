using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace ASPAssistant.Core.Services;

public sealed record MuMuExtrasSettings(string Path, int Index, string Library, string Config);

public static class MuMuExtras
{
    public static MuMuExtrasSettings? Resolve(EmulatorDevice device, Func<string, bool>? exists = null,
        Func<string, string>? readManager = null)
    {
        if (device.Kind != "mumu" || device.GamePackage is null) return null;
        exists ??= File.Exists;
        var config = JsonNode.Parse(device.Config)!.AsObject();
        var extras = config["extras"] as JsonObject;
        var mumu = extras?["mumu"] as JsonObject;
        var root = mumu?["path"]?.GetValue<string>();
        var index = mumu?["index"]?.GetValue<int>() ?? 0;
        var library = mumu?["lib"]?.GetValue<string>();
        string? FindLibrary(string directory) => new[] { "nx_main", "shell" }
            .Select(folder => System.IO.Path.Combine(directory, folder, "sdk", "external_renderer_ipc.dll"))
            .FirstOrDefault(exists);
        if (root is null)
        {
            root = System.IO.Path.GetDirectoryName(device.AdbPath);
            for (var level = 0; level <= 2 && root is not null; level++)
            {
                library = FindLibrary(root);
                if (library is not null) break;
                root = System.IO.Path.GetDirectoryName(root);
            }
            if (root is null || library is null) return null;
            var manager = new[] { System.IO.Path.Combine(root, "MuMuManager.exe"),
                System.IO.Path.Combine(root, "shell", "MuMuManager.exe") }.FirstOrDefault(exists);
            int? managerIndex = null;
            if (manager is not null)
            {
                var output = readManager is not null ? readManager(manager)
                    : System.Text.Encoding.UTF8.GetString(EmulatorDisplay.RunAdb(
                        device with { AdbPath = manager }, ["info", "-v", "all"]).Output);
                if (!string.IsNullOrWhiteSpace(output))
                    managerIndex = FindManagerIndex(JsonNode.Parse(output), device.Serial.Split(':').Last());
            }
            // 管理器未提供匹配实例时，按 NX 端口布局推断。
            if (!int.TryParse(device.Serial.Split(':').Last(), out var port)
                || (managerIndex is null && (port < 16384 || port > 65535 || (port - 16384) % 32 != 0))) return null;
            index = managerIndex ?? (port - 16384) / 32;
        }
        library ??= FindLibrary(root);
        if (library is null) return null;
        if (extras is null) config["extras"] = extras = new JsonObject();
        if (mumu is null) extras["mumu"] = mumu = new JsonObject();
        mumu["enable"] = true;
        mumu["path"] = root;
        mumu["index"] = index;
        mumu["app_package"] = device.GamePackage;
        mumu["app_cloned_index"] = 0;
        return new(root, index, library, config.ToJsonString());
    }

    private static int? FindManagerIndex(JsonNode? node, string port)
    {
        if (node is JsonObject obj)
        {
            if (obj["adb_port"]?.ToString() == port && int.TryParse(obj["index"]?.ToString(), out var index))
                return index;
            foreach (var child in obj)
                if (FindManagerIndex(child.Value, port) is { } found) return found;
        }
        if (node is JsonArray array)
            foreach (var child in array)
                if (FindManagerIndex(child, port) is { } found) return found;
        return null;
    }

    // 仅在创建控制器前调用；异常由截图候选的失败去重统一记录。
    public static string Prepare(EmulatorDevice device, MuMuExtrasSettings settings,
        Func<MuMuExtrasSettings, string, int>? query = null)
    {
        var display = (query ?? QueryDisplay)(settings, device.GamePackage!);
        if (display < 0 || (display == 0 && device.DisplayId != 0))
            throw new IOException($"nemu_get_display_id={display}; dumpsys display={device.DisplayId}");
        return settings.Config;
    }

    // 签名依据 EmulatorExtras 54d3a3a 的 MuMu SDK 头文件：int 句柄、UTF-16 路径、UTF-8 包名。
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Connect([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDisplay(int handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string package, int clone);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Disconnect(int handle);

    public static int QueryDisplay(MuMuExtrasSettings settings, string package)
    {
        var library = NativeLibrary.Load(settings.Library);
        try
        {
            var connect = Marshal.GetDelegateForFunctionPointer<Connect>(NativeLibrary.GetExport(library, "nemu_connect"));
            var query = Marshal.GetDelegateForFunctionPointer<GetDisplay>(NativeLibrary.GetExport(library, "nemu_get_display_id"));
            var disconnect = Marshal.GetDelegateForFunctionPointer<Disconnect>(NativeLibrary.GetExport(library, "nemu_disconnect"));
            var handle = connect(settings.Path, settings.Index);
            if (handle <= 0) throw new IOException($"nemu_connect={handle}");
            try { return query(handle, package, 0); }
            finally { disconnect(handle); }
        }
        finally { NativeLibrary.Free(library); }
    }
}
