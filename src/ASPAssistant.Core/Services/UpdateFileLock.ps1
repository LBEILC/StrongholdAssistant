# 只在重试用尽时查询占用者。Restart Manager 查询失败时保留原始文件错误。
function Get-FileOwners([string]$path) {
    Add-Type -TypeDefinition @'
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

public static class UpdateLockOwners {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Info {
        public int Pid;
        public System.Runtime.InteropServices.ComTypes.FILETIME Started;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Service;
        public uint Type, Status, Session;
        public int Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmStartSession(out uint session, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmRegisterResources(uint session, uint count, string[] files,
        uint apps, IntPtr processes, uint services, IntPtr names);
    [DllImport("rstrtmgr.dll")]
    static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] Info[] info, out uint reasons);
    [DllImport("rstrtmgr.dll")]
    static extern int RmEndSession(uint session);

    public static string Find(string path) {
        uint session, needed, count = 0, reasons;
        if (RmStartSession(out session, 0, new StringBuilder(33)) != 0) return "";
        try {
            if (RmRegisterResources(session, 1, new[] { path }, 0, IntPtr.Zero, 0, IntPtr.Zero) != 0 ||
                RmGetList(session, out needed, ref count, null, out reasons) != 234) return "";
            var list = new Info[needed];
            count = needed;
            if (RmGetList(session, out needed, ref count, list, out reasons) != 0) return "";
            return string.Join(", ", list.Take((int)count).Select(p => p.Name + " (PID " + p.Pid + ")"));
        } finally { RmEndSession(session); }
    }
}
'@
    [UpdateLockOwners]::Find($path)
}

function Open-UpdateFile($path, $mode, $access, $share) {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        try { return [IO.File]::Open($path, $mode, $access, $share) }
        catch {
            $reason = $_.Exception.GetBaseException().Message
            if ($wait.Elapsed.TotalSeconds -ge 15) {
                try { $owners = Get-FileOwners $path } catch { $owners = '' }
                if ($owners) { $reason += "；占用进程：$owners" }
                throw "无法打开 $path（已等待 15 秒）：$reason"
            }
            if ($wait.ElapsedMilliseconds -lt 500) { Write-Log "waiting for file: $path" }
            Start-Sleep -Milliseconds 500
        }
    }
}
