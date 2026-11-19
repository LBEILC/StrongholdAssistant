using System.Runtime.InteropServices;
using ASPAssistant.Core.Services;
using MaaFramework.Binding;
using MaaFramework.Binding.Abstractions;
using Moq;

namespace StrongholdAssistant.Tests;

public sealed class PcWindowCaptureTests
{
    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [Fact]
    public void HideBorderDisposesSessionAndSkipsCreationUntilDisabled()
    {
        // 只借用有效窗口句柄；连接和截图作业全由测试替身处理。
        var window = GetDesktopWindow();
        var controller = new Mock<IMaaController>();
        var jobs = new Mock<IMaaPost>();
        var link = new MaaJob(1, jobs.Object);
        var frame = new MaaJob(2, jobs.Object);
        jobs.Setup(j => j.Wait(link)).Returns(MaaJobStatus.Succeeded);
        jobs.Setup(j => j.Wait(frame)).Returns(MaaJobStatus.Failed);
        controller.Setup(c => c.LinkStart()).Returns(link);
        controller.Setup(c => c.Screencap()).Returns(frame);
        int opened = 0;
        using var capture = new PcWindowCapture(_ => { opened++; return controller.Object; });

        capture.Capture(window);
        controller.Verify(c => c.Screencap(), Times.Once);
        capture.SetHideCaptureBorder(true);
        controller.Verify(c => c.Dispose(), Times.Once);
        Assert.Null(capture.Capture(window));
        Assert.Equal(1, opened);
        Assert.Equal("bitblt", capture.Method);
        Assert.Contains("去掉黄框", capture.LastFallbackReason);

        capture.SetHideCaptureBorder(false);
        capture.Capture(window);
        Assert.Equal(2, opened);
        controller.Verify(c => c.Screencap(), Times.Exactly(2));
    }
}
