using Devlog.Core.Abstractions;
using Devlog.Infrastructure.Git;
using Devlog.Infrastructure.Migrations;
using Devlog.Infrastructure.Persistence;
using Devlog.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Devlog.Infrastructure;

/// <summary>
/// Infrastructure registers its own services, so the host never has to know that
/// storage happens to be SQLite or that foreground capture happens to be Win32.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddDevlogInfrastructure(this IServiceCollection services)
    {
        // Storage
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<MigrationRunner>();

        // Source-of-truth stores
        services.AddSingleton<IEventStore, EventStore>();
        services.AddSingleton<OverrideStore>();
        services.AddSingleton<ClassificationRuleStore>();

        // Same instance, exposed a second way — the interface Devlog.Core
        // defines, so Devlog.Api can depend on it without a reference to this
        // (Windows-only) project. RecordSightingsAsync stays derivation-internal
        // and is reached only through the concrete type above.
        services.AddSingleton<IClassificationRuleStore>(sp => sp.GetRequiredService<ClassificationRuleStore>());

        // Derived stores — rebuilt wholesale on every derivation
        services.AddSingleton<ActivityStore>();
        services.AddSingleton<SessionStore>();

        // The read half. Separate from the writers above because it is the one
        // thing both the terminal and the API consume — one query, two
        // renderers, so they cannot disagree about what a session was.
        services.AddSingleton<ISessionReader, SessionReader>();

        // Git enrichment. Re-scannable rather than append-only: --scan-git
        // rebuilds rows from disk, --derive re-links them with no disk access.
        services.AddSingleton<ICommitStore, CommitStore>();
        services.AddSingleton<GitScanner>();

        // Destructive maintenance, separate from the append-only event store.
        services.AddSingleton<MaintenanceStore>();

        // Capture. Singletons because both own an OS hook whose lifetime must
        // match the process, not a scope.
        services.AddSingleton<IActivityWatcher, WinEventForegroundWatcher>();
        services.AddSingleton<SessionSwitchMonitor>();

        return services;
    }
}
