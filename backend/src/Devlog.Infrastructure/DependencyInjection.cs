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

        // Derived stores — rebuilt wholesale on every derivation
        services.AddSingleton<ActivityStore>();
        services.AddSingleton<SessionStore>();

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
