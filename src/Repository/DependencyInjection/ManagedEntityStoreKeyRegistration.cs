namespace Forge.Repository.DependencyInjection;

/// <summary>
/// Marker registered by a platform-managed entity DI helper (e.g. <c>AddForgeBranch()</c>)
/// to declare that the named keyed store is a managed-entity store that must have aspect
/// enforcement applied when <c>AddForgeAspects()</c> is active.
/// <para>
/// Consumed by <c>ManagedEntityAspectValidationService</c> in <c>Forge.Aspects</c>, which
/// validates at startup that every <see cref="ManagedEntityStoreKeyRegistration"/> has a
/// matching <c>AspectEnforcedKeyedStoreRegistration</c>. See root ADR-0019.
/// </para>
/// </summary>
/// <param name="StoreKey">The keyed-service key of the managed-entity store.</param>
public sealed record ManagedEntityStoreKeyRegistration(string StoreKey);
