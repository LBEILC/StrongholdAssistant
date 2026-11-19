using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

public class InstallerUpdateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothInstallationsPreferMatchingDelta(bool installed)
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            if (installed) File.WriteAllText(Path.Combine(root.FullName, "installed.json"), "{}");
            var service = new UpdateService(new Version(1, 0, 0), root.FullName);
            const string full = "StrongholdAssistant-v1.1.0-win-x64.zip";
            const string delta = "StrongholdAssistant-v1.1.0-from-v1.0.0-delta.zip";
            Assert.Equal(delta, service.SelectPackageAsset(new Version(1, 1, 0), [full, delta], ""));
            Assert.Equal(full, service.SelectPackageAsset(new Version(1, 1, 0), [full], ""));
        }
        finally { root.Delete(true); }
    }
}
