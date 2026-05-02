using Forge.Entity;
namespace Forge.Aspects;

/// <summary>
/// The operation kinds to which an aspect may apply. Used when registering an aspect
/// and when resolving whether a declared aspect is valid for a given operation.
/// </summary>
[Flags]
public enum AspectKind
{
    /// <summary>Applies to Create operations.</summary>
    Create = 1,

    /// <summary>Applies to Update (Replace) operations.</summary>
    Update = 2,

    /// <summary>Applies to Delete operations.</summary>
    Delete = 4,

    /// <summary>Applies to Read operations (LoadAsync, QueryByTypeAsync, LINQ, dynamic SPARQL).</summary>
    Read = 8,
}
