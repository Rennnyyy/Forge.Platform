namespace Forge.Entity.Messaging;

/// <summary>
/// Classifies the mutation that triggered an entity change event.
/// See root ADR-0021.
/// </summary>
public enum EntityChangeOperation
{
    /// <summary>The entity was created for the first time.</summary>
    Created,

    /// <summary>The entity was replaced in full (PUT semantics).</summary>
    Updated,

    /// <summary>The entity was deleted from the store.</summary>
    Deleted,
}
