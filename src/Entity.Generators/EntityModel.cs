using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Forge.Entity.Generators;

/// <summary>Identity strategy mirrored from <c>Forge.Entity.IdentityGenerator</c>.</summary>
internal enum IdentityStrategy { Path, UuidV4, UuidV5, Inherited }

/// <summary>Kind of an entity reference property.</summary>
internal enum RefKind { OwningSingle, OwningCollection, InverseSingle, InverseCollection }

/// <summary>One identity-input property on an entity.</summary>
internal sealed record IdentityPartModel(
    string PropertyName,
    string TypeDisplayName,
    bool IsString,
    int Order,
    string Separator);

/// <summary>One reference property on an entity.</summary>
internal sealed record RefModel(
    string PropertyName,
    string TargetTypeFullName,
    string TargetTypeDisplayName,
    RefKind Kind,
    string Predicate,
    /// <summary>For inverse: the owning property's name on the target. Null for owning sides.</summary>
    string? OwningPropertyName,
    /// <summary>For owning collections: whether to emit <c>DeferredEntityRefCollectionImpl</c>.</summary>
    bool IsLazy = false);

/// <summary>Parsed shape of one [Entity]-marked partial class.</summary>
internal sealed record EntityModel(
    string FullyQualifiedName,
    string Namespace,
    string TypeName,
    bool IsSealed,
    bool HasParameterlessCtor,
    string? Path,
    string? PredicatePath,
    IdentityStrategy IdentityStrategy,
    string? IdentityNamespace,
    bool IsEnumeration,
    ImmutableArray<IdentityPartModel> IdentityParts,
    ImmutableArray<RefModel> References,
    Location DeclarationLocation,
    bool IsPartial,
    string? BaseEntityTypeFqn,
    string? BaseEntityTypeDisplayName)
{
    public bool IsEntitySubtype => BaseEntityTypeFqn is not null;
    public string FileName => Namespace.Length == 0 ? $"{TypeName}.g.cs" : $"{Namespace}.{TypeName}.g.cs";
}

/// <summary>Cross-entity registry: target type FQN -> its inverse properties keyed by owning property name.</summary>
internal sealed class EntityRegistry
{
    private readonly Dictionary<string, EntityModel> _byFqn;

    public EntityRegistry(IEnumerable<EntityModel> models)
    {
        _byFqn = models.ToDictionary(m => m.FullyQualifiedName);
    }

    /// <summary>
    /// Find the inverse property declared on <paramref name="targetTypeFqn"/> whose
    /// <c>[Inverse(PropertyName=...)]</c> matches <paramref name="owningPropertyName"/> on the owner type.
    /// Returns the property name and whether it is a collection inverse.
    /// </summary>
    public (string PropertyName, bool IsCollection)? FindInverse(string targetTypeFqn, string owningPropertyName)
    {
        if (!_byFqn.TryGetValue(targetTypeFqn, out var target)) return null;
        var match = target.References.FirstOrDefault(r =>
            (r.Kind == RefKind.InverseSingle || r.Kind == RefKind.InverseCollection)
            && r.OwningPropertyName == owningPropertyName);
        if (match is null) return null;
        return (match.PropertyName, match.Kind == RefKind.InverseCollection);
    }

    /// <summary>Kept for compatibility — returns only single inverse setters.</summary>
    public string? FindInverseSetter(string targetTypeFqn, string owningPropertyName)
        => FindInverse(targetTypeFqn, owningPropertyName) is { IsCollection: false } r ? r.PropertyName : null;
}
