using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

public class LdPlayerWindowsTests
{
    [Fact]
    public void CreatesDevicesFromListOutput()
    {
        var directory = @"C:\Users\你\LDPlayer";
        var output = LdPlayerWindows.OutputEncoding.GetBytes("""
            0,雷电模拟器,0,0,1,62328,55792
            2,雷电模拟器-2,0,0,1,62329,55793,1920,1080,280
            """);
        var devices = LdPlayerWindows.FromListOutput(directory, output);
        Assert.Equal(new[] { "emulator-5554", "emulator-5558" }, devices.Select(d => d.Serial));
        Assert.Equal(new[] { "雷电模拟器", "雷电模拟器-2" }, devices.Select(d => d.Name));
        Assert.All(devices, device => Assert.Equal(Path.Combine(directory, "adb.exe"), device.AdbPath));
    }

    [Fact]
    public void ParsesSevenAndTenColumnsWithGbkTitlesAndSkipsStoppedInstances()
    {
        var output = LdPlayerWindows.OutputEncoding.GetBytes("""
            0,雷电模拟器,7407882,989172,1,62328,55792
            1,雷电模拟器-1,1234,5678,1,62329,55793,1920,1080,280
            2,未启动,0,0,0,-1,-1
            """);
        var windows = LdPlayerWindows.Parse(output);
        Assert.Equal(2, windows.Count);
        Assert.Equal(new(0, "雷电模拟器", (IntPtr)7407882, (IntPtr)989172), windows[0]);
        Assert.Equal(new(1, "雷电模拟器-1", (IntPtr)1234, (IntPtr)5678), windows[1]);
    }
}
