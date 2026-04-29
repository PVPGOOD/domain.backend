using Domain.Watchdog.Agent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Domain.Backend.Watchdog;

public sealed class DomainBackendWatchdogAgent
{
    private WebApplication? _app;
    private IHostApplicationLifetime? _applicationLifetime;
    private WatchdogAgentStatusReporter? _statusReporter;

    public void Attach(WebApplication app)
    {
        _app = app;
        _applicationLifetime = app.Lifetime;
        _statusReporter = app.Services.GetRequiredService<WatchdogAgentStatusReporter>();
    }

    public ValueTask<WatchdogAgentResult> OnUpdatePreparingAsync(
        WatchdogAgentEventContext context,
        CancellationToken cancellationToken)
    {
        _app?.Logger.LogInformation(
            "Watchdog Agent update preparing accepted. DeploymentId={DeploymentId}, Version={Version}",
            context.DeploymentId,
            context.Version);

        return ValueTask.FromResult(WatchdogAgentResult.Accept());
    }

    public ValueTask<WatchdogAgentResult> OnUpdateCommittedAsync(
        WatchdogAgentEventContext context,
        CancellationToken cancellationToken)
    {
        _app?.Logger.LogInformation(
            "Watchdog Agent update committed. DeploymentId={DeploymentId}, Version={Version}",
            context.DeploymentId,
            context.Version);

        return ValueTask.FromResult(WatchdogAgentResult.Accept());
    }

    public ValueTask<WatchdogAgentResult> OnUpdateRolledBackAsync(
        WatchdogAgentEventContext context,
        CancellationToken cancellationToken)
    {
        _app?.Logger.LogWarning(
            "Watchdog Agent update rolled back. DeploymentId={DeploymentId}, Version={Version}, Reason={Reason}",
            context.DeploymentId,
            context.Version,
            context.Reason);

        return ValueTask.FromResult(WatchdogAgentResult.Accept());
    }

    public ValueTask<WatchdogAgentResult> OnShutdownAsync(
        WatchdogAgentEventContext context,
        CancellationToken cancellationToken)
    {
        _app?.Logger.LogInformation(
            "Watchdog Agent shutdown requested. DeploymentId={DeploymentId}, Version={Version}, Reason={Reason}",
            context.DeploymentId,
            context.Version,
            context.Reason);

        _ = ReportShutdownAsync("requested", context.Reason, cancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            _app?.Logger.LogInformation(
                "Watchdog Agent shutdown hook is stopping Domain.Backend. DeploymentId={DeploymentId}",
                context.DeploymentId);
            await ReportShutdownAsync("stopping-by-hook", $"DeploymentId={context.DeploymentId}", cancellationToken);
            _applicationLifetime?.StopApplication();
        }, cancellationToken);

        return ValueTask.FromResult(WatchdogAgentResult.Accept("Shutdown requested."));
    }

    public Task ReportStartupAsync(CancellationToken cancellationToken = default)
    {
        return _statusReporter?.ReportStartupAsync(BuildInfo.GitCommit, BuildInfo.BuildTimeUtc, cancellationToken) ?? Task.CompletedTask;
    }

    public Task ReportHealthAsync(string? remoteIp, CancellationToken cancellationToken = default)
    {
        return _statusReporter?.ReportHealthAsync(BuildInfo.GitCommit, BuildInfo.BuildTimeUtc, remoteIp, cancellationToken) ?? Task.CompletedTask;
    }

    public Task ReportShutdownAsync(string status, string? message, CancellationToken cancellationToken = default)
    {
        return _statusReporter?.ReportShutdownAsync(status, BuildInfo.GitCommit, BuildInfo.BuildTimeUtc, message, cancellationToken) ?? Task.CompletedTask;
    }
}

public static class DomainBackendWatchdogAgentExtensions
{
    public static IServiceCollection AddDomainBackendWatchdogAgent(
        this IServiceCollection services,
        IConfiguration configuration,
        DomainBackendWatchdogAgent agent)
    {
        services.AddWatchdogAgent(options =>
        {
            options.ApplicationId = DomainBackendWatchdogConstants.ApplicationId;
            options.SharedSecret = configuration["WatchdogAgent:SharedSecret"];
            options.WatchdogBaseUrl = configuration["WatchdogAgent:WatchdogBaseUrl"];
            options.ReportSharedSecret = configuration["WatchdogAgent:ReportSharedSecret"];
            if (int.TryParse(configuration["WatchdogAgent:ReportTimeoutSeconds"], out var reportTimeoutSeconds))
            {
                options.ReportTimeoutSeconds = reportTimeoutSeconds;
            }

            options.OnUpdatePreparing = agent.OnUpdatePreparingAsync;
            options.OnUpdateCommitted = agent.OnUpdateCommittedAsync;
            options.OnUpdateRolledBack = agent.OnUpdateRolledBackAsync;
            options.OnShutdown = agent.OnShutdownAsync;
        });

        return services;
    }

    public static WebApplication UseDomainBackendWatchdogAgent(
        this WebApplication app,
        IConfiguration configuration,
        DomainBackendWatchdogAgent agent)
    {
        agent.Attach(app);

        app.Logger.LogInformation(
            "Watchdog Agent ready. ApplicationId={ApplicationId}, Endpoint={Endpoint}, HealthEndpoint={HealthEndpoint}, SharedSecretConfigured={SharedSecretConfigured}",
            DomainBackendWatchdogConstants.ApplicationId,
            DomainBackendWatchdogConstants.AgentEventEndpoint,
            DomainBackendWatchdogConstants.AgentHealthEndpoint,
            IsSharedSecretConfigured(configuration));

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            app.Logger.LogInformation(
                "Watchdog Agent startup message. ApplicationId={ApplicationId}, GitCommit={GitCommit}, BuildTimeUtc={BuildTimeUtc}, AgentStatus={AgentStatus}",
                DomainBackendWatchdogConstants.ApplicationId,
                BuildInfo.GitCommit,
                BuildInfo.BuildTimeUtc,
                "started");
            _ = Task.Run(() => agent.ReportStartupAsync());
        });

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            app.Logger.LogInformation(
                "Watchdog Agent shutdown message. ApplicationId={ApplicationId}, GitCommit={GitCommit}, BuildTimeUtc={BuildTimeUtc}, AgentStatus={AgentStatus}",
                DomainBackendWatchdogConstants.ApplicationId,
                BuildInfo.GitCommit,
                BuildInfo.BuildTimeUtc,
                "stopping");
            _ = Task.Run(() => agent.ReportShutdownAsync("stopping", "ApplicationStopping fired."));
        });

        app.Lifetime.ApplicationStopped.Register(() =>
        {
            app.Logger.LogInformation(
                "Watchdog Agent shutdown message. ApplicationId={ApplicationId}, GitCommit={GitCommit}, BuildTimeUtc={BuildTimeUtc}, AgentStatus={AgentStatus}",
                DomainBackendWatchdogConstants.ApplicationId,
                BuildInfo.GitCommit,
                BuildInfo.BuildTimeUtc,
                "stopped");
            _ = Task.Run(() => agent.ReportShutdownAsync("stopped", "ApplicationStopped fired."));
        });

        return app;
    }

    public static WebApplication MapDomainBackendWatchdogEndpoints(
        this WebApplication app,
        IConfiguration configuration,
        DomainBackendWatchdogAgent agent)
    {
        app.MapGet("/health", async (HttpContext httpContext) =>
        {
            app.Logger.LogInformation(
                "Watchdog Agent health message. ApplicationId={ApplicationId}, GitCommit={GitCommit}, BuildTimeUtc={BuildTimeUtc}, AgentStatus={AgentStatus}, RemoteIp={RemoteIp}",
                DomainBackendWatchdogConstants.ApplicationId,
                BuildInfo.GitCommit,
                BuildInfo.BuildTimeUtc,
                "healthy",
                httpContext.Connection.RemoteIpAddress?.ToString());
            await agent.ReportHealthAsync(httpContext.Connection.RemoteIpAddress?.ToString(), httpContext.RequestAborted);

            return Results.Ok(new
            {
                status = "ok",
                service = DomainBackendWatchdogConstants.ApplicationId,
                gitCommit = BuildInfo.GitCommit,
                buildTimeUtc = BuildInfo.BuildTimeUtc,
                watchdogAgent = new
                {
                    applicationId = DomainBackendWatchdogConstants.ApplicationId,
                    status = "ready",
                    eventEndpoint = DomainBackendWatchdogConstants.AgentEventEndpoint,
                    healthEndpoint = DomainBackendWatchdogConstants.AgentHealthEndpoint,
                    sharedSecretConfigured = IsSharedSecretConfigured(configuration)
                }
            });
        });

        app.MapWatchdogAgent();
        return app;
    }

    private static bool IsSharedSecretConfigured(IConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration["WatchdogAgent:SharedSecret"]);
    }
}

internal static class DomainBackendWatchdogConstants
{
    public const string ApplicationId = "Domain.Backend";
    public const string AgentEventEndpoint = "/_watchdog/agent/events/{eventName}";
    public const string AgentHealthEndpoint = "/_watchdog/agent/health";
}
