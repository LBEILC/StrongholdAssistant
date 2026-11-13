using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;

namespace StrongholdAssistant.Host;

internal static class ProcessElevation
{
    public static bool Current { get; } = IsElevated(Environment.ProcessId);
    public static bool KeysBlocked(bool elevated, bool gameElevated) => !elevated && gameElevated;

    public static bool ForWindow(nint window)
    {
        GetWindowThreadProcessId(window, out var pid);
        return IsElevated((int)pid);
    }

    private static bool IsElevated(int pid)
    {
        var process = OpenProcess(0x1000, false, pid);
        if (process == 0) return Marshal.GetLastWin32Error() == 5;
        try
        {
            if (!OpenProcessToken(process, 8, out var token)) return Marshal.GetLastWin32Error() == 5;
            try
            {
                if (!GetTokenInformation(token, 20, out int elevated, 4, out _))
                    throw new Win32Exception();
                return elevated != 0;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    public static void RestartElevated()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--wait-for-exit") { i++; continue; }
            if (args[i] != "--keep-identity") start.ArgumentList.Add(args[i]);
        }
        start.ArgumentList.Add("--wait-for-exit");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        try { using var child = Process.Start(start) ?? throw new InvalidOperationException("无法启动助手。"); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new ArgumentException("没有同意管理员授权，助手还是普通身份运行。", ex); }
    }

    public static async Task WaitForPreviousAsync(string[] args)
    {
        int index = Array.IndexOf(args, "--wait-for-exit");
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out int pid)) return;
        Process previous;
        try { previous = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (previous) await previous.WaitForExitAsync();
    }

    // 同一用户的普通进程可激活管理员实例并发送链接，不开放给其他用户。
    public static void AllowSameUser(SafeHandle handle)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sddl = $"D:P(A;;GA;;;{identity.User!.Value})S:(ML;;NW;;;ME)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw new Win32Exception();
        try
        {
            if (!SetKernelObjectSecurity(handle, 4 | 0x10, descriptor)) throw new Win32Exception();
        }
        finally { LocalFree(descriptor); }
    }

    public static NamedPipeServerStream CreateDeepLinkPipe(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sddl = $"D:P(A;;GA;;;{identity.User!.Value})S:(ML;;NW;;;ME)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw new Win32Exception();
        try
        {
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            // 创建时赋予中完整性标签，允许同一用户的浏览器向管理员实例发送链接。
            var pipe = CreateNamedPipe(@"\\.\pipe\" + name, 0x40000001, 8, 1, 4096, 4096, 0, ref security);
            if (pipe.IsInvalid) { pipe.Dispose(); throw new Win32Exception(); }
            return new NamedPipeServerStream(PipeDirection.In, true, false, pipe);
        }
        finally { LocalFree(descriptor); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public nint Descriptor; public int Inherit; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint access, uint mode, uint instances, uint outputSize, uint inputSize, uint timeout, ref SecurityAttributes security);

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(nint token, int info, out int value, int length, out int returned);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out nint descriptor, out uint size);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetKernelObjectSecurity(SafeHandle handle, uint info, nint descriptor);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint value);
}
