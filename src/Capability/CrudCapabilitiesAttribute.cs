namespace Forge.Capability;

/// <summary>
/// Opts an <c>[Entity]</c>-annotated partial class into CRUD capability generation.
/// The <c>Forge.Capability.Generators</c> source generator reads this attribute and
/// emits command records, response records, and
/// <see cref="ICapabilityHandler{TCommand,TResponse}"/> implementations that delegate
/// to the entity's active-record methods (<c>CreateAsync</c>, <c>ReadAsync</c>, etc.)
/// produced by <c>Forge.Operations.Generators</c>.
/// </summary>
/// <remarks>
/// Each generated handler carries a <c>[Capability("{path}.{operation}")]</c> identity
/// derived from the entity's <c>[Entity(Path = "…")]</c> value (or the lowercased class
/// name when <c>Path</c> is absent).
/// </remarks>
/// <seealso cref="CrudMethod"/>
/// See Capability ADR-0012.
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CrudCapabilitiesAttribute : Attribute
{
    /// <summary>
    /// Initialises the attribute requesting the specified CRUD operations.
    /// Defaults to <see cref="CrudMethod.All"/> when omitted.
    /// </summary>
    public CrudCapabilitiesAttribute(CrudMethod methods = CrudMethod.All)
    {
        Methods = methods;
    }

    /// <summary>The set of CRUD operations to generate handlers for.</summary>
    public CrudMethod Methods { get; }
}
