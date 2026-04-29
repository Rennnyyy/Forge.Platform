namespace Forge.Entity;

/// <summary>
/// Common base class for all entities. Provides identity sealing, equality by IRI,
/// and the hook the source-generated half uses to materialize the IRI.
/// </summary>
public abstract class EntityBase : IEntity, IEquatable<EntityBase>
{
    private string? _iri;

    public string Iri
    {
        get
        {
            EnsureIdentity();
            return _iri ?? throw new InvalidOperationException(
                "IRI has not been materialized yet. Either provide enough identity parts or call MaterializeIdentity().");
        }
        protected internal set
        {
            if (_iri is not null && !string.Equals(_iri, value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"IRI is sealed ('{_iri}'); create a new entity to change identity.");
            }
            _iri = value;
        }
    }

    /// <summary>
    /// Called automatically before every <see cref="Iri"/> read.
    /// Overridden by the generator for <c>Path</c> and <c>PropertyBasedEncoded</c> entities
    /// to call <c>MaterializeIdentity()</c> on first access.
    /// </summary>
    protected virtual void EnsureIdentity() { }

    public bool IsIdentitySealed => _iri is not null;

    /// <summary>
    /// Called by the generated half whenever an identity-relevant property changes.
    /// Throws if the IRI is already sealed.
    /// </summary>
    protected internal void GuardIdentityMutation()
    {
        if (_iri is not null)
        {
            throw new InvalidOperationException(
                $"Identity property cannot be changed: IRI '{_iri}' is sealed.");
        }
    }

    /// <summary>
    /// Used by the loader/serializer during hydration to assign the persisted IRI directly.
    /// Bypasses identity-part recomputation but still respects sealing.
    /// </summary>
    protected internal void HydrateIri(string iri)
    {
        if (_iri is not null && !string.Equals(_iri, iri, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot hydrate IRI on an already-sealed entity.");
        }
        _iri = iri;
    }

    public bool Equals(EntityBase? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        EnsureIdentity();
        other.EnsureIdentity();
        if (_iri is null || other._iri is null) return false;
        return string.Equals(_iri, other._iri, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => obj is EntityBase e && Equals(e);

    public override int GetHashCode()
    {
        EnsureIdentity();
        return _iri is null ? 0 : HashCode.Combine(GetType(), _iri);
    }

    public override string ToString() =>
        _iri is null ? $"{GetType().Name}(<unmaterialized>)" : _iri;
}
