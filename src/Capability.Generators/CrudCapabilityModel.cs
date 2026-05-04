using System.Collections.Immutable;
using System.Linq;

namespace Forge.Capability.Generators;

/// <summary>One property (identity-part or predicate) on an entity.</summary>
internal sealed record PropModel(
    string Name,
    string TypeDisplay,
    bool IsInitOnly,
    int Order = 0);

/// <summary>Parsed shape of one <c>[Entity]</c>+<c>[CrudCapabilities]</c>-decorated class.</summary>
internal sealed record CrudCapabilityEntityModel(
    string FullyQualifiedName,
    string Namespace,
    string TypeName,
    string CapabilityPathSegment,
    int Methods,
    ImmutableArray<PropModel> IdentityProps,
    ImmutableArray<PropModel> DataProps)
{
    public bool HasCreate => (Methods & 1) != 0;
    public bool HasRead   => (Methods & 2) != 0;
    public bool HasUpdate => (Methods & 4) != 0;
    public bool HasDelete => (Methods & 8) != 0;
    public bool HasList   => (Methods & 16) != 0;

    /// <summary>
    /// Emit <c>Read{T}Response</c> when either Read or List is requested, since the
    /// response record doubles as the list item type.
    /// </summary>
    public bool NeedsReadResponse => HasRead || HasList;

    /// <summary>Properties for the Create command: identity parts + all predicate props.</summary>
    public ImmutableArray<PropModel> CreateCommandProps => IdentityProps.AddRange(DataProps);

    /// <summary>
    /// Properties for the Update command: only settable (not init-only) predicate props.
    /// Identity parts are init-only by convention and cannot be changed after creation.
    /// </summary>
    public ImmutableArray<PropModel> UpdateCommandProps =>
        DataProps.Where(p => !p.IsInitOnly).ToImmutableArray();

    /// <summary>All props in declaration order for the Read response and List item.</summary>
    public ImmutableArray<PropModel> ReadResponseProps => IdentityProps.AddRange(DataProps);

    public string FileName => Namespace.Length == 0
        ? $"{TypeName}.g.caps.cs"
        : $"{Namespace}.{TypeName}.g.caps.cs";
}
