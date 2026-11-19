using System.Runtime.InteropServices;

namespace ASPAssistant.Core.Interop;

internal static class CaptureBorderDiagnostic
{
    internal static void Log() => AppLogger.Info("ScreenCapture", Read(RequestBorderlessAccess));

    internal static string Read(Func<string> requestAccess)
    {
        string property = "Unknown";
        string access;
        try
        {
            RoInitialize(1); // 后台线程使用多线程单元，与截图线程无关。
            try
            {
                var metadata = Factory<IApiInformationStatics>("Windows.Foundation.Metadata.ApiInformation");
                using var session = new HString("Windows.Graphics.Capture.GraphicsCaptureSession");
                using var name = new HString("IsBorderRequired");
                property = (metadata.IsPropertyPresent(session.Value, name.Value) != 0).ToString();

                access = requestAccess();
            }
            finally { RoUninitialize(); }
        }
        catch (Exception ex)
        {
            access = $"Exception({ex.GetType().Name}, 0x{ex.HResult:X8})";
        }
        return $"WindowsBuild={Environment.OSVersion.Version.Build} IsBorderRequiredSupported={property} BorderlessAccess={access}";
    }

    private static string RequestBorderlessAccess()
    {
        var capture = Factory<IGraphicsCaptureAccessStatics>("Windows.Graphics.Capture.GraphicsCaptureAccess");
        var operation = capture.RequestAccessAsync(0); // GraphicsCaptureAccessKind.Borderless
        var completed = new Completion();
        operation.SetCompleted(completed);
        completed.Task.GetAwaiter().GetResult();
        return operation.GetResults().ToString();
    }

    private static T Factory<T>(string className)
    {
        using var name = new HString(className);
        var iid = typeof(T).GUID;
        RoGetActivationFactory(name.Value, in iid, out var factory);
        return (T)factory;
    }

    private sealed class HString(string text) : IDisposable
    {
        public nint Value { get; } = WindowsCreateString(text, text.Length);
        public void Dispose() => WindowsDeleteString(Value);
    }

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void RoInitialize(int type);
    [DllImport("combase.dll")]
    private static extern void RoUninitialize();
    [DllImport("combase.dll", PreserveSig = false, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint WindowsCreateString(string text, int length);
    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void RoGetActivationFactory(nint name, in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    // 接口布局来自 Windows SDK 的 windows.foundation.metadata.h 和 windows.graphics.capture.h。
    // _VtblGap 跳过 IInspectable 的三个方法及目标方法之前未使用的 SDK 方法。
    [ComImport, Guid("997439fe-f681-4a11-b416-c13a47e8ba36"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApiInformationStatics
    {
        void _VtblGap1_7();
        byte IsPropertyPresent(nint type, nint property);
    }

    [ComImport, Guid("743ed370-06ec-5040-a58a-901f0f757095"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureAccessStatics
    {
        void _VtblGap1_3();
        IAccessOperation RequestAccessAsync(int kind);
    }

    [ComImport, Guid("827caf42-5fe6-5b5b-84ce-c44834134d3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccessOperation
    {
        void _VtblGap1_3();
        void SetCompleted(ICompletion handler);
        void _VtblGap2_1();
        AccessStatus GetResults();
    }

    private enum AccessStatus { DeniedBySystem, NotDeclaredByApp, DeniedByUser, UserPromptRequired, Allowed }

    [ComVisible(true), Guid("6ea0f2e9-bc97-58e8-a3a6-c829b9e5f2aa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICompletion
    {
        void Invoke(nint operation, int status);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class Completion : ICompletion
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Task => _completed.Task;
        public void Invoke(nint operation, int status) => _completed.TrySetResult();
    }
}
