using Microsoft.CodeAnalysis;

namespace Forge.Entity.Generators;

/// <summary>
/// Diagnostic descriptors emitted by the entity analyzer/generator.
/// IDs are stable across refactors; new ones must be added at the end.
/// </summary>
internal static class EntityDiagnostics
{
    public static readonly DiagnosticDescriptor MissingPartial = new(
        id: "FORGE0001",
        title: "Entity class must be declared partial",
        messageFormat: "Class '{0}' is annotated with [Entity] but is not declared 'partial'.",
        category: "Forge.Entity",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingIdentity = new(
        id: "FORGE0002",
        title: "Entity class must declare exactly one [Identity]",
        messageFormat: "Class '{0}' is annotated with [Entity] but does not declare an [Identity(...)] attribute.",
        category: "Forge.Entity",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IdentityPartTypeNotAllowed = new(
        id: "FORGE0003",
        title: "Identity part property uses a disallowed type",
        messageFormat: "Property '{0}.{1}' has type '{2}' which is not allowed as an [IdentityPart].",
        category: "Forge.Entity",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingInverseTarget = new(
        id: "FORGE0004",
        title: "[Inverse] target property does not exist on referenced entity",
        messageFormat: "Property '{0}.{1}' is marked [Inverse] of '{2}.{3}' but no such [Owning] property exists there.",
        category: "Forge.Entity",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UuidV5MissingNamespace = new(
        id: "FORGE0005",
        title: "PropertyBasedEncoded: explicit Namespace is not a valid GUID",
        messageFormat: "Class '{0}' uses [Identity(IdentityGenerator.PropertyBasedEncoded)] with an explicit Namespace value that cannot be parsed as a GUID. Either fix the value or omit it to use the auto-derived namespace (UuidV5 of BaseIri + Path).",
        category: "Forge.Entity",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
