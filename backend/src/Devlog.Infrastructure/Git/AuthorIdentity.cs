using Devlog.Core.Configuration;
using LibGit2Sharp;

namespace Devlog.Infrastructure.Git;

/// <summary>
/// The set of email addresses that count as "you" for scanning purposes.
/// <para>
/// This exists because a single fixed email is wrong on this machine: <c>devlog</c>
/// commits under a personal GitHub noreply address, while every work repo
/// commits under a work email. Collecting <c>user.email</c> from each configured
/// repo — rather than assuming one global identity — is what makes both count.
/// </para>
/// </summary>
public static class AuthorIdentity
{
    /// <summary>
    /// Reads <c>user.email</c> from every configured repo plus the global
    /// config, unions it with <see cref="GitOptions.AuthorEmails"/>, and
    /// case-insensitively dedupes. A repo whose local config is unset falls back
    /// to global automatically — that is how git resolves it too.
    /// </summary>
    public static HashSet<string> Resolve(GitOptions options)
    {
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var repo in options.Repos)
        {
            if (!System.IO.Directory.Exists(repo.Path))
            {
                continue;
            }

            try
            {
                using var git = new Repository(repo.Path);
                var email = git.Config.Get<string>("user.email")?.Value;

                if (!string.IsNullOrWhiteSpace(email))
                {
                    emails.Add(email.Trim());
                }
            }
            catch (RepositoryNotFoundException)
            {
                // Configured path is not actually a git repo. The scanner
                // reports this per-repo; identity collection just skips it.
            }
        }

        foreach (var email in options.AuthorEmails)
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                emails.Add(email.Trim());
            }
        }

        return emails;
    }
}
