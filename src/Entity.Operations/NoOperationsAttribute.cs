namespace Forge.Entity.Operations;

/// <summary>
/// Opt-out marker: instructs <c>Forge.Entity.Operations.Generators</c> to skip emitting
/// the active-record CRUD partial file (<c>.g.ops.cs</c>) for the decorated
/// <c>[Entity]</c> class.
/// </summary>
/// <remarks>
/// Apply this attribute when a type participates in structural generation
/// (<c>Forge.Entity.Generators</c>) but provides its own hand-written active-record
/// surface, for example to support extra behaviour around persistence.
/// The opt-out is per-type and non-inheritable.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class NoOperationsAttribute : Attribute { }
