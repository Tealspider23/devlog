using System.Security.Cryptography;
using System.Text;
using Devlog.Core.Configuration;

namespace Devlog.Api.Security;

/// <summary>
/// Lets the AI provider's API key be set from the Settings page instead of
/// only via the gitignored <c>appsettings.local.json</c>.
/// <para>
/// Unlike <see cref="ApiTokenStore"/>'s plaintext convention, this is a real
/// third-party secret rather than a token devlog generates and controls
/// itself, so it is encrypted at rest with Windows DPAPI
/// (<see cref="DataProtectionScope.CurrentUser"/>) — only this Windows user
/// account can decrypt it. Stored beside the database as
/// <c>ai-api-key.dat</c>, gitignored, never logged.
/// </para>
/// <para>
/// On construction, a stored key overrides whatever <c>AiOptions.ApiKey</c>
/// was bound from config — mutating the shared singleton in place, which
/// <c>ChatClassifier</c> reads fresh on every call, so the change is live
/// with no restart. If no file exists yet, the config-bound value (if any)
/// is left untouched.
/// </para>
/// </summary>
public sealed class AiKeyStore
{
    private readonly AiOptions _ai;

    public string KeyPath { get; }

    public AiKeyStore(DevlogOptions options, AiOptions ai)
    {
        _ai = ai;

        var directory = Path.GetDirectoryName(options.ResolveDatabasePath())!;
        KeyPath = Path.Combine(directory, "ai-api-key.dat");

        if (File.Exists(KeyPath))
        {
            var protectedBytes = File.ReadAllBytes(KeyPath);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            _ai.ApiKey = Encoding.UTF8.GetString(plainBytes);
        }
    }

    public void Save(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (File.Exists(KeyPath))
            {
                File.Delete(KeyPath);
            }

            _ai.ApiKey = null;
            return;
        }

        var plainBytes = Encoding.UTF8.GetBytes(apiKey);
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(KeyPath, protectedBytes);

        _ai.ApiKey = apiKey;
    }
}
