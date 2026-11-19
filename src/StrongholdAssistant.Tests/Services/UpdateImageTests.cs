using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

[Collection("Update environment")]
public class UpdateImageTests
{
    private static byte[] Package(params (string Name, string Text)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var file in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(file.Name).Open());
                writer.Write(file.Text);
            }
        return memory.ToArray();
    }

    [Fact]
    public void MissingCategoriesAreEmptyAndIncompletePatchDoesNotChangeInstallation()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var app = root.CreateSubdirectory("installation").FullName;
            var original = Path.Combine(app, "old.txt");
            File.WriteAllText(original, "old");
            var zip = Path.Combine(root.FullName, "delta.zip");
            File.WriteAllBytes(zip, Package(("changes.json", """{"deleted":["old.txt"],"added":["new.txt"]}"""), ("new.txt", "new")));
            var image = Path.Combine(root.FullName, "image");
            UpdateImage.Prepare(zip, image, app, true, false);
            Assert.False(File.Exists(Path.Combine(image, "old.txt")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(image, "new.txt")));

            File.WriteAllBytes(zip, Package(("changes.json", """{"deleted":["old.txt"],"added":["missing"]}""")));
            Assert.Throws<InvalidDataException>(() => UpdateImage.Prepare(zip, image + "2", app, true, false));
            Assert.Equal("old", File.ReadAllText(original));
        }
        finally { root.Delete(true); }
    }

    [Theory]
    [InlineData(false, "../outside")]
    [InlineData(true, "C:/outside")]
    [InlineData(true, "dir/../outside")]
    public async Task UnsafeDeltaFallsBackToVerifiedFullPackage(bool manifest, string path)
    {
        var bad = manifest ? Package(("changes.json", $$"""{"deleted":["{{path}}"]}""")) : Package((path, "bad"));
        var full = Package(("StrongholdAssistant.exe", "full"));
        using var http = new HttpClient(new Packages(bad, full));
        var root = Directory.CreateTempSubdirectory();
        var service = new UpdateService(new Version(1, 0, 0), root.FullName, httpClient: http);
        UpdateInfo Info(string url, byte[] bytes) => new("v2.0.0", "2.0.0", "", "https://example.org/" + url, url, null)
        { Source = "mirrorchyan", Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        try
        {
            var info = Info("delta", bad) with { Incremental = true, FullPackage = _ => Task.FromResult<UpdateInfo?>(Info("full", full)) };
            await service.DownloadAndApplyUpdateAsync(info);
            var extracted = Path.Combine(Path.GetDirectoryName(service.PendingScriptPath!)!, "extracted");
            Assert.Equal("full", File.ReadAllText(Path.Combine(extracted, "StrongholdAssistant.exe")));
            Assert.Empty(root.EnumerateFileSystemInfos());
        }
        finally
        {
            if (service.PendingScriptPath is { } script) Directory.Delete(Path.GetDirectoryName(script)!, true);
            root.Delete(true);
        }
    }

    private sealed class Packages(byte[] delta, byte[] full) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(request.RequestUri!.AbsolutePath == "/delta" ? delta : full) });
    }
}
