using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Forge.Branch;

/// <summary>
/// Populates the <see cref="SnapshotGuardedTransactionalStore"/> frozen-IRI set at
/// application start by querying all <see cref="Snapshot"/> entities from the management
/// graph. Follows the same startup pattern as <see cref="DefaultBranchStartupService"/>.
/// See Branch ADR-0002 (startup-load + flush-on-write strategy).
/// </summary>
internal sealed class SnapshotStartupService(
    [FromKeyedServices("forge.branch.management")] SnapshotGuardedTransactionalStore snapshotGuard,
    ILogger<SnapshotStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Loading snapshot IRIs into immutability guard.");
        await snapshotGuard.InvalidateFrozenSetAsync(cancellationToken).ConfigureAwait(false);
        logger.LogDebug("Snapshot immutability guard initialised.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
