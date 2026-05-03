using Forge.Entity;
using System.Reflection;

namespace Forge.Repository.Mapping;

/// <summary>
/// Resolves predicate-IRI strings declared on <c>[Owning]</c>, <c>[Inverse]</c>, and
/// <c>[Predicate]</c> against <see cref="EntityOptions.Current"/> and the entity's
/// <c>[Entity(PredicatePath)]</c>. Mirrors the rules in ADR-0005 (entity slice).
/// </summary>
internal static class PredicateResolver
{
    public static string Resolve(string declared, string? predicatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declared);
        // Absolute IRI (any colon in the value, e.g. "schema:name" or "http://...").
        if (declared.Contains(':')) return declared;
        var basePart = EntityOptions.Current.PredicateBaseIri.TrimEnd('/');
        if (string.IsNullOrEmpty(predicatePath))
            return $"{basePart}/{declared}";
        return $"{basePart}/{predicatePath!.Trim('/')}/{declared}";
    }

    public static string Resolve(string declared, Type entityType)
    {
        var entityAttr = entityAttrCache.GetOrAdd(entityType,
            static t => t.GetCustomAttribute<EntityAttribute>());
        var predicatePath = entityAttr?.PredicatePath ?? entityAttr?.Path;
        return Resolve(declared, predicatePath);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, EntityAttribute?> entityAttrCache = new();
}
