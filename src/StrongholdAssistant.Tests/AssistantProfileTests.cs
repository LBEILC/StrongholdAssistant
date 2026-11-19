using System.IO;
using ASPAssistant.Core;
using StrongholdAssistant.Host;

namespace ASPAssistant.Tests;

// AppLogger 与启动诊断测试共用进程静态状态。
[CollectionDefinition("配置文件", DisableParallelization = true)]
public class ProfileCollection { }

[Collection("配置文件")]
public class AssistantProfileTests
{
    [Theory]
    [InlineData("")]
    [InlineData("../other")]
    [InlineData("agent名字")]
    [InlineData("abcdefghijklmnopqrstuvwxy")]
    [InlineData("CON")]
    public void RejectsInvalidName(string name) =>
        Assert.Throws<ArgumentException>(() => AssistantProfile.Current.Named(name));

    [Fact]
    public void ProfilesSeedSettingsOnceAndKeepSeparateLogs()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var original = new AssistantProfile(root.FullName, null);
            Assert.Equal(root.FullName, original.DirectoryPath);
            Assert.Equal(@"Local\StrongholdAssistant", original.KernelName(""));
            var settings = new HostSettingsStore(Path.Combine(original.DirectoryPath, "settings.json"));
            settings.SaveRunAsAdmin(false);
            var agent = original.Named("Agent-1");
            agent.Create(false);
            var copy = new HostSettingsStore(Path.Combine(agent.DirectoryPath, "settings.json"));
            Assert.False(copy.LoadSettings().RunAsAdmin);
            Assert.Equal("", copy.LoadSettings().Routes.Hotkey);
            Assert.Equal("", copy.LoadSettings().ToggleHotkey);
            Assert.Equal("Alt+S", settings.LoadSettings().ToggleHotkey);
            copy.SaveRunAsAdmin(true);
            agent.Create(true);
            Assert.True(copy.LoadSettings().RunAsAdmin);
            Assert.False(settings.LoadSettings().RunAsAdmin);
            Assert.Equal(new[] { "agent-1" }, original.List());
            foreach (var profile in new[] { original, agent })
            {
                AppLogger.Initialize(Path.Combine(profile.DirectoryPath, "logs"));
                AppLogger.Info("ProfileTest", profile.Name ?? "default-only");
            }
            var logs = File.ReadAllText(Path.Combine(agent.DirectoryPath, "logs", "host.log"));
            Assert.Contains("agent-1", logs);
            Assert.DoesNotContain("default-only", logs);
            Assert.DoesNotContain("agent-1", File.ReadAllText(Path.Combine(root.FullName, "logs", "host.log")));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void ReopeningSameNameSignalsOnlyItsOwner()
    {
        var profile = AssistantProfile.Current.Named(Guid.NewGuid().ToString("N")[..24]);
        var duplicate = AssistantProfile.Current.Named(profile.Name!.ToUpperInvariant());
        using var first = new Mutex(false, profile.KernelName(""), out var isFirst);
        using var second = new Mutex(false, duplicate.KernelName(""), out var isSecond);
        Assert.True(isFirst);
        Assert.False(isSecond);
        using var receive = new EventWaitHandle(false, EventResetMode.AutoReset, profile.KernelName(".Activate"));
        using var send = EventWaitHandle.OpenExisting(duplicate.KernelName(".Activate"));
        send.Set();
        Assert.True(receive.WaitOne(0));
    }
}
