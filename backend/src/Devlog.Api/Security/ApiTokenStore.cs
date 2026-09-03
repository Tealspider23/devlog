using System.Security.Cryptography;
using Devlog.Core.Configuration;

namespace Devlog.Api.Security;

/// <summary>
/// The password the API requires on every request but <c>/health</c>.
/// <para>
/// Without this, any page you visit could ask <c>127.0.0.1:5111</c> for your
/// activity log and the browser would let it — loopback is not a privilege
/// boundary between websites. The token is what makes it one.
/// </para>
/// <para>
/// Generated once and persisted beside the database, so the address stays
/// stable across collector restarts rather than invalidating a saved token on
/// every run. <c>api-token.txt</c> is gitignored — see <c>.gitignore</c>.
/// </para>
/// </summary>
public sealed class ApiTokenStore
{
    public string Token { get; }

    public string TokenPath { get; }

    public ApiTokenStore(DevlogOptions options)
    {
        var directory = Path.GetDirectoryName(options.ResolveDatabasePath())!;
        TokenPath = Path.Combine(directory, "api-token.txt");

        Token = File.Exists(TokenPath)
            ? File.ReadAllText(TokenPath).Trim()
            : Generate(TokenPath);
    }

    private static string Generate(string path)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(path, token);
        return token;
    }
}
