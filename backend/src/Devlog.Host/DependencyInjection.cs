using Devlog.Core.Configuration;
using Devlog.Host.Derivation;
using Devlog.Host.Diagnostics;
using Devlog.Host.HostedServices;
using Devlog.Host.Tray;
using Devlog.Infrastructure;
using Serilog;

namespace Devlog.Host;

/// <summary>
/// One place where the application is composed. Program.cs stays a thin entry
/// point that decides which command to run, rather than a growing list of
/// registrations.
/// </summary>
internal static class DependencyInjection
{
    public static IHostApplicationBuilder AddDevlog(this IHostApplicationBuilder builder)
    {
        var options = builder.BindOptions();
        builder.AddDevlogLogging(options);

        builder.Services.AddDevlogInfrastructure();
        builder.Services.AddDevlogHostServices();

        return builder;
    }

    /// <summary>
    /// Options are bound eagerly and registered as instances rather than through
    /// <c>IOptions&lt;T&gt;</c>: they are read on the capture hot path, and none
    /// of them are meant to change without a restart.
    /// </summary>
    private static DevlogOptions BindOptions(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration
            .GetSection(DevlogOptions.SectionName)
            .Get<DevlogOptions>() ?? new DevlogOptions();

        var derivation = builder.Configuration
            .GetSection(DerivationOptions.SectionName)
            .Get<DerivationOptions>() ?? new DerivationOptions();

        var git = builder.Configuration
            .GetSection(GitOptions.SectionName)
            .Get<GitOptions>() ?? new GitOptions();

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(derivation);
        builder.Services.AddSingleton(git);

        return options;
    }

    private static void AddDevlogLogging(this IHostApplicationBuilder builder, DevlogOptions options)
    {
        var logDirectory = Path.Combine(
            Path.GetDirectoryName(options.ResolveDatabasePath())!,
            "logs");

        builder.Services.AddSerilog((_, cfg) => cfg
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDirectory, "devlog-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,

                // More than one devlog process touches this file: the collector
                // runs all day while --stats, --derive and --seed come and go, and
                // the UI will join them later. Without shared:true the second
                // writer silently loses its output — exactly how the collector's
                // own startup lines went missing during Phase 1 verification.
                shared: true));
    }

    private static IServiceCollection AddDevlogHostServices(this IServiceCollection services)
    {
        services.AddSingleton<PauseController>();
        services.AddSingleton<StatsReporter>();
        services.AddSingleton<DerivationRunner>();
        services.AddSingleton<GitScanRunner>();

        services.AddHostedService<CollectorService>();

        return services;
    }
}
