using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using ASPAssistant.Core.Interop;

namespace StrongholdAssistant.Host;

// 保留原类名；改用原始键盘输入，不拦截按键、不依赖前台线程的键状态。
internal sealed class KeyboardHook : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;
    public KeyboardHook(Action<uint, bool, uint, nint> observe)
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            using var source = new HwndSource(new HwndSourceParameters("KeyboardInput") { ParentWindow = new nint(-3) });
            var pressed = new HashSet<uint>();
            source.AddHook((nint hwnd, int message, nint wparam, nint lparam, ref bool handled) =>
            {
                if (message != 0xFF) return 0;
                uint size = (uint)Marshal.SizeOf<RawKeyboardInput>();
                uint read = GetRawInputData(lparam, 0x10000003, out var input, ref size, (uint)Marshal.SizeOf<RawHeader>());
                if (read == uint.MaxValue || read < Marshal.SizeOf<RawHeader>() + 16 || input.Header.Type != 1 || input.Key == 255) return 0;
                uint key = input.Key switch
                {
                    0x10 => input.ScanCode == 0x36 ? 0xA1u : 0xA0u,
                    0x11 => (input.Flags & 2) != 0 ? 0xA3u : 0xA2u,
                    0x12 => (input.Flags & 2) != 0 ? 0xA5u : 0xA4u,
                    _ => input.Key
                };
                bool down = (input.Flags & 1) == 0;
                if (!(down ? pressed.Add(key) : pressed.Remove(key))) return 0;
                bool Has(uint left, uint right) => pressed.Contains(left) || pressed.Contains(right);
                uint modifiers = (Has(0xA2, 0xA3) ? 2u : 0) | (Has(0xA4, 0xA5) ? 1u : 0)
                    | (Has(0xA0, 0xA1) ? 4u : 0) | (Has(0x5B, 0x5C) ? 8u : 0);
                observe(key, down, modifiers, User32.GetForegroundWindow());
                // 留给默认窗口过程清理原始输入；不使用 NOLEGACY，不影响游戏收键。
                return 0;
            });
            if (!RegisterRawInputDevices([new() { UsagePage = 1, Usage = 6, Flags = 0x100, Target = source.Handle }], 1, (uint)Marshal.SizeOf<RawDevice>()))
            {
                ready.SetException(new Win32Exception(Marshal.GetLastWin32Error()));
                return;
            }
            ready.SetResult(dispatcher);
            try { Dispatcher.Run(); }
            finally { RegisterRawInputDevices([new() { UsagePage = 1, Usage = 6, Flags = 1 }], 1, (uint)Marshal.SizeOf<RawDevice>()); }
        }) { IsBackground = true, Name = "KeyboardInput" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _dispatcher = ready.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        _thread.Join();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawHeader { public uint Type, Size; public nint Device, Wparam; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboardInput
    {
        public RawHeader Header;
        public ushort ScanCode, Flags, Reserved, Key;
        public uint Message, Extra;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(nint raw, uint command, out RawKeyboardInput data, ref uint size, uint headerSize);
}
