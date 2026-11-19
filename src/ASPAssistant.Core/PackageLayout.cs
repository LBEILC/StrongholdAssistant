namespace ASPAssistant.Core;

public static class PackageLayout
{
    private static readonly DirectoryInfo Host = new(AppContext.BaseDirectory);
    public static string Root => Host.Name.Equals("app", StringComparison.OrdinalIgnoreCase)
        ? Host.Parent!.FullName : Host.FullName;
    public static string Launcher => Path.Combine(Root, "StrongholdAssistant.exe");
}
