using Forge.Aspects.Operation;
using Forge.Repository.DependencyInjection;
using Forge.Repository.Transaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Forge.Aspects.DependencyInjection;

/// <summary>
/// Marker registered by <see cref="AspectsServiceCollectionExtensions.AddForgeAspectsForKeyedStore"/>
/// to confirm that aspect enforcement has been applied to a specific keyed store.
/// Consumed by <see cref="ManagedEntityAspectValidationService"/> at startup.
/// See root ADR-0019.
/// </summary>
/// <param name="StoreKey">The keyed-service key that has been wrapped with aspect enforcement.</param>
public sealed record AspectEnforcedKeyedStoreRegistration(string StoreKey);

/// <summary>
/// Startup validator that ensures every <see cref="ManagedEntityStoreKeyRegistration"/>
/// has a matching <see cref="AspectEnforcedKeyedStoreRegistration"/>.
/// Only registered when <c>AddForgeAspects()</c> is active (i.e. when
/// <see cref="IAspectStore"/> appears in the container).
/// See root ADR-0019.
/// </summary>
internal sealed class ManagedEntityAspectValidationService : IHostedService
{
    private readonly IEnumerable<ManagedEntityStoreKeyRegistration> _managed;
    private readonly IEnumerable<AspectEnforcedKeyedStoreRegistration> _enforced;

    public ManagedEntityAspectValidationService(
        IEnumerable<ManagedEntityStoreKeyRegistration> managed,
        IEnumerable<AspectEnforcedKeyedStoreRegistration> enforced)
    {
        _managed = managed;
        _enforced = enforced;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var enforcedKeys = new HashSet<string>(
            _enforced.Select(r => r.StoreKey),
            StringComparer.Ordinal);

        var missing = _managed
            .Select(r => r.StoreKey)
            .Where(k => !enforcedKeys.Contains(k))
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"One or more platform-managed entity stores are registered without aspect " +
                $"enforcement. Call AddForgeAspectsForKeyedStore() for each of the following " +
                $"keyed stores, or use the corresponding Http DI helper " +
                $"(e.g. AddForgeBranchHttp instead of AddForgeBranch). " +
                $"Missing keys: {string.Join(", ", missing.Select(k => $"'{k}'"))}. " +
                $"See root ADR-0019.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
