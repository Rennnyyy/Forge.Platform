using Forge.Repository;
using Forge.Repository.Transaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forge.Branch;

/// <summary>
/// Ensures the default <see cref="Branch"/> entity exists in the management graph at
/// application start. Idempotent: if the branch already exists the service is a no-op.
/// See Branch ADR-0001 (startup upsert pattern).
/// </summary>
internal sealed class DefaultBranchStartupService(
    [FromKeyedServices("forge.branch.management")] ITransactionalEntityStore managementStore,
    IOptions<BranchOptions> branchOptions,
    ILogger<DefaultBranchStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = branchOptions.Value;
        var defaultIri = opts.DefaultBranchIri;

        if (string.IsNullOrEmpty(defaultIri))
        {
            logger.LogWarning(
                "BranchOptions.DefaultBranchIri is empty at startup. " +
                "Ensure AddForgeBranch() has been called with a valid configuration.");
            return;
        }

        // Check whether the default branch entity already exists.
        var repo = new EntityRepository<Branch>(managementStore);
        var existing = await repo.FindAsync(defaultIri, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            logger.LogDebug("Default branch '{BranchIri}' already exists; skipping upsert.", defaultIri);
            return;
        }

        // Derive the Name property from the default branch IRI's last path segment.
        // The IRI pattern is {BaseIri}/branches/{Name}, so the last segment is the name.
        var name = DeriveNameFromIri(defaultIri);

        var defaultBranch = new Branch
        {
            Name = name,
            Description = "Default branch (auto-created at startup)",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        logger.LogInformation(
            "Creating default branch '{BranchName}' ({BranchIri}) in management graph '{ManagementGraph}'.",
            name, defaultIri, opts.ManagementGraphIri);

        await using var tx = new EntityTransaction(managementStore);
        tx.Create<Branch>(defaultBranch);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Default branch '{BranchName}' created successfully.", name);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string DeriveNameFromIri(string iri)
    {
        var uri = new Uri(iri, UriKind.Absolute);
        var lastSegment = uri.Segments[^1].TrimEnd('/');
        return string.IsNullOrEmpty(lastSegment) ? iri : lastSegment;
    }
}
