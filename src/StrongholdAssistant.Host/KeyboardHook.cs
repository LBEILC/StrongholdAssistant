using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ASPAssistant.Core.Interop;

namespace StrongholdAssistant.Host;

internal sealed class KeyboardHook : IDisposable
{
    private readonly HookProc _callback;
    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;
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
        // UI 初始化和绘制可能阻塞；低级钩子必须有独立消息循环，避免超时后被系统静默移除。
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var handle = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
            if (handle == 0)
            {
                ready.SetException(new Win32Exception(Marshal.GetLastWin32Error()));
                return;
            }
            ready.SetResult(dispatcher);
            try { Dispatcher.Run(); }
            finally { UnhookWindowsHookEx(handle); }
        }) { IsBackground = true, Name = "KeyboardHook" };
        _thread.Start();
        _dispatcher = ready.Task.GetAwaiter().GetResult();
    }
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public void Dispose()
    {
        _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        _thread.Join();
    }
    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
