using System.Collections.Concurrent;

namespace Forge.Entity.Repository;

/// <summary>Resolves <see cref="IRdfMapper{T}"/> by entity type and by <c>rdf:type</c> IRI.</summary>
public interface IRdfMapperRegistry
{
    IRdfMapper<T> For<T>() where T : class, IEntity;
    IRdfMapper? ForTypeIri(string typeIri, EntityRepositoryOptions options);
    IEnumerable<IRdfMapper> All { get; }
}

/// <summary>
/// Default registry. Mappers are discovered at registration time (via DI extensions)
/// or constructed lazily through <see cref="ReflectionRdfMapper{T}"/>.
/// </summary>
public sealed class RdfMapperRegistry : IRdfMapperRegistry
{
    private readonly ConcurrentDictionary<Type, IRdfMapper> _byType = new();

    public RdfMapperRegistry() { }

    public RdfMapperRegistry(IEnumerable<IRdfMapper> mappers)
    {
        foreach (var m in mappers) _byType[m.EntityType] = m;
    }

    public void Register(IRdfMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _byType[mapper.EntityType] = mapper;
    }

    public IRdfMapper<T> For<T>() where T : class, IEntity
    {
        var mapper = _byType.GetOrAdd(typeof(T), static t =>
        {
            var ctor = typeof(ReflectionRdfMapper<>).MakeGenericType(t);
            return (IRdfMapper)Activator.CreateInstance(ctor)!;
        });
        return (IRdfMapper<T>)mapper;
    }

    public IRdfMapper? ForTypeIri(string typeIri, EntityRepositoryOptions options)
    {
        foreach (var m in _byType.Values)
            if (string.Equals(m.ResolveTypeIri(options), typeIri, StringComparison.Ordinal))
                return m;
        return null;
    }

    public IEnumerable<IRdfMapper> All => _byType.Values;
}
