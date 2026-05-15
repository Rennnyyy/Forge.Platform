namespace Forge.Structure;

/// <summary>
/// Marker interface that signals an entity participates as a node in a variant-configuration
/// structure tree. Implementing this interface has no runtime behaviour; it serves as a
/// documentation and tooling signal that the entity can appear as the parent or child of a
/// <see cref="Usage"/>. See Variant ADR-0001.
/// </summary>
public interface IStructure { }
