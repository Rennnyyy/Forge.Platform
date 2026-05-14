using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Forge.Authorization.DependencyInjection;

/// <summary>
/// One-shot startup service that emits a <see cref="LogLevel.Warning"/> when
/// <see cref="AllowAllAspectGuard"/> is the effective guard registered by
/// <see cref="AuthorizationServiceCollectionExtensions.AddForgeAuthorization"/>.
/// Provides an observable signal in structured logs so operators can detect
/// permissive authorization before a deployment reaches production.
/// See Authorization ADR-0007.
/// </summary>
internal sealed class AllowAllGuardWarningService(ILogger<AllowAllGuardWarningService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Forge Authorization: AllowAllAspectGuard is active — every write operation is " +
            "permitted unconditionally. Register an explicit IAspectGuard via " +
            "AddForgeAuthorization(yourGuard) before deploying to a production environment. " +
            "To suppress this warning in non-production environments, set the " +
            "'Forge.Authorization' log category to a level above Warning.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
