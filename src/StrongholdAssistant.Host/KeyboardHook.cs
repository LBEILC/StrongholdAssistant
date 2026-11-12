using System.ComponentModel;
using System.Runtime.InteropServices;
using ASPAssistant.Core.Interop;

namespace StrongholdAssistant.Host;

internal sealed class KeyboardHook : IDisposable
{
    private readonly HookProc _callback;
    private readonly nint _handle;
    public KeyboardHook(Action<uint, bool, uint, nint> observe)
    {
        _callback = (code, message, data) =>
        {
            if (code >= 0 && message.ToInt64() is 0x100 or 0x101 or 0x104 or 0x105)
            {
                uint key = unchecked((uint)Marshal.ReadInt32(data));
                uint modifiers = (Down(0x11) ? 2u : 0) | (Down(0x12) ? 1u : 0)
                    | (Down(0x10) ? 4u : 0) | (Down(0x5B) || Down(0x5C) ? 8u : 0);
                observe(key, message.ToInt64() is 0x100 or 0x104, modifiers, User32.GetForegroundWindow());
            }
            return CallNextHookEx(0, code, message, data);
        };
        _handle = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public void Dispose() => UnhookWindowsHookEx(_handle);
    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
