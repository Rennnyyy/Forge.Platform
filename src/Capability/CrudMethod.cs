namespace Forge.Capability;

/// <summary>
/// Flags that control which CRUD operations <see cref="CrudCapabilitiesAttribute"/>
/// instructs the source generator to emit. Combine values to request a subset.
/// </summary>
/// <seealso cref="CrudCapabilitiesAttribute"/>
[Flags]
public enum CrudMethod
{
    /// <summary>Emit <c>Create{T}Handler</c>.</summary>
    Create = 1,

    /// <summary>Emit <c>Read{T}Handler</c> and <c>Read{T}Response</c>.</summary>
    Read = 2,

    /// <summary>Emit <c>Update{T}Handler</c>.</summary>
    Update = 4,

    /// <summary>Emit <c>Delete{T}Handler</c>.</summary>
    Delete = 8,

    /// <summary>Emit <c>List{T}Handler</c> (uses <c>Read{T}Response</c> as the item type).</summary>
    List = 16,

    /// <summary>Emit all five CRUD handlers.</summary>
    All = Create | Read | Update | Delete | List,
}
