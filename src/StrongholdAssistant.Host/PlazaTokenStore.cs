using System.Security.Cryptography;
using System.Text;

namespace StrongholdAssistant.Host;

public sealed class PlazaTokenStore(string userDirectory)
{
    private readonly string _path = Path.Combine(userDirectory, "plaza-token.bin");

    public void Save(string token)
    {
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(userDirectory);
        File.WriteAllBytes(_path, encrypted);
    }

    public string? Read()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException) { return null; }
    }

    public void Delete() => File.Delete(_path);
}
