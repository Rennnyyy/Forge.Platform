namespace Forge.Capability;

/// <summary>
/// Marks an <see cref="ICapabilityHandler{TCommand,TResponse}"/> implementation as a
/// CRUD-generated handler, produced by the <c>Forge.Capability.Generators</c> source
/// generator for entities carrying <see cref="CrudCapabilitiesAttribute"/>.
/// <para>
/// The HTTP transport (<c>Forge.Capability.Http</c>) reads this attribute to route the
/// handler under the <c>api/entities/</c> prefix instead of the general
/// <c>api/capabilities/</c> prefix. Hand-written handlers may also carry this attribute
/// to opt in to the entity route prefix explicitly.
/// </para>
/// See Capability ADR-0013.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CrudCapabilityHandlerAttribute : Attribute { }
