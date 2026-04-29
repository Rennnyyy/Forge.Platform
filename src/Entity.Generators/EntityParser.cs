using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Forge.Entity.Generators;

internal static class EntityParser
{
    private const string EntityAttr = "Forge.Entity.EntityAttribute";
    private const string IdentityAttr = "Forge.Entity.IdentityAttribute";
    private const string IdentityPartAttr = "Forge.Entity.IdentityPartAttribute";
    private const string OwningAttr = "Forge.Entity.OwningAttribute";
    private const string InverseAttr = "Forge.Entity.InverseAttribute";
    private const string EnumerationAttr = "Forge.Entity.EnumerationAttribute";

    public static EntityModel? TryParse(
        INamedTypeSymbol type,
        ClassDeclarationSyntax decl,
        SourceProductionContext ctx)
    {
        var entityAttr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EntityAttr);
        if (entityAttr is null) return null;

        var location = decl.Identifier.GetLocation();

        // FORGE0001: must be partial.
        var isPartial = decl.Modifiers.Any(m => m.Text == "partial");
        if (!isPartial)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                EntityDiagnostics.MissingPartial, location, type.Name));
            return null;
        }

        // FORGE0002: exactly one [Identity].
        var identityAttr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == IdentityAttr);
        if (identityAttr is null)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                EntityDiagnostics.MissingIdentity, location, type.Name));
            return null;
        }

        var Path = entityAttr.NamedArguments.FirstOrDefault(kv => kv.Key == "Path").Value.Value as string;
        var explicitPredicatePath = entityAttr.NamedArguments.FirstOrDefault(kv => kv.Key == "PredicatePath").Value.Value as string;
        // Default PredicatePath to Path when not set explicitly.
        var predicatePath = string.IsNullOrEmpty(explicitPredicatePath) ? Path : explicitPredicatePath;

        var strategy = (IdentityStrategy)System.Convert.ToInt32(
            identityAttr.ConstructorArguments.Length > 0 ? identityAttr.ConstructorArguments[0].Value : 0);
        var identityNamespace = identityAttr.NamedArguments.FirstOrDefault(kv => kv.Key == "Namespace").Value.Value as string;

        var isEnumeration = type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == EnumerationAttr);

        var identityParts = ImmutableArray.CreateBuilder<IdentityPartModel>();
        var references = ImmutableArray.CreateBuilder<RefModel>();

        foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
        {
            // Identity part?
            var partAttr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == IdentityPartAttr);
            if (partAttr is not null)
            {
                var order = System.Convert.ToInt32(
                    partAttr.ConstructorArguments.Length > 0 ? partAttr.ConstructorArguments[0].Value : 0);
                var separator = partAttr.NamedArguments.FirstOrDefault(kv => kv.Key == "Separator").Value.Value as string ?? "/";
                var typeDisplay = member.Type.ToDisplayString();

                if (!IsAllowedIdentityPartType(member.Type))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        EntityDiagnostics.IdentityPartTypeNotAllowed,
                        member.Locations.FirstOrDefault() ?? location,
                        type.Name, member.Name, typeDisplay));
                }

                identityParts.Add(new IdentityPartModel(
                    member.Name,
                    typeDisplay,
                    member.Type.SpecialType == SpecialType.System_String,
                    order,
                    separator));
                continue;
            }

            // Owning?
            var owningAttr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == OwningAttr);
            if (owningAttr is not null)
            {
                var predicate = owningAttr.ConstructorArguments.Length > 0
                    ? owningAttr.ConstructorArguments[0].Value as string ?? ""
                    : "";

                if (TryClassifyRef(member.Type, out var refKind, out var targetFqn, out var targetDisplay)
                    && refKind != RefKind.InverseSingle)
                {
                    var isLazy = owningAttr.NamedArguments.Any(kv =>
                        kv.Key == "Lazy" && kv.Value.Value is true);
                    references.Add(new RefModel(
                        member.Name, targetFqn, targetDisplay, refKind, predicate,
                        OwningPropertyName: null, IsLazy: isLazy));
                }
                continue;
            }

            // Inverse?
            var inverseAttr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == InverseAttr);
            if (inverseAttr is not null)
            {
                var owningPropertyName = inverseAttr.ConstructorArguments.Length > 0
                    ? inverseAttr.ConstructorArguments[0].Value as string ?? ""
                    : "";
                var predicate = inverseAttr.ConstructorArguments.Length > 1
                    ? inverseAttr.ConstructorArguments[1].Value as string ?? ""
                    : "";

                if (TryClassifyRef(member.Type, out var inverseKind, out var targetFqn, out var targetDisplay))
                {
                    // [Inverse] on an EntityRefCollection<T> → InverseCollection; otherwise InverseSingle.
                    var resolvedKind = inverseKind == RefKind.OwningCollection
                        ? RefKind.InverseCollection
                        : RefKind.InverseSingle;
                    var isLazy = resolvedKind == RefKind.InverseCollection
                        && inverseAttr.NamedArguments.Any(kv => kv.Key == "Lazy" && kv.Value.Value is true);
                    references.Add(new RefModel(
                        member.Name, targetFqn, targetDisplay, resolvedKind, predicate,
                        owningPropertyName, IsLazy: isLazy));
                }
            }
        }

        // FORGE0005: if an explicit namespace is provided it must be a parseable GUID.
        if (strategy == IdentityStrategy.UuidV5
            && !string.IsNullOrWhiteSpace(identityNamespace)
            && !System.Guid.TryParse(identityNamespace, out _))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                EntityDiagnostics.UuidV5MissingNamespace, location, type.Name));
        }

        var ns = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();

        var hasParameterless = type.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility != Accessibility.Private);

        return new EntityModel(
            FullyQualifiedName: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace: ns,
            TypeName: type.Name,
            IsSealed: type.IsSealed,
            HasParameterlessCtor: hasParameterless,
            Path: Path,
            PredicatePath: predicatePath,
            IdentityStrategy: strategy,
            IdentityNamespace: identityNamespace,
            IsEnumeration: isEnumeration,
            IdentityParts: identityParts.OrderBy(p => p.Order).ToImmutableArray(),
            References: references.ToImmutable(),
            DeclarationLocation: location,
            IsPartial: true);
    }

    /// <summary>Classify the property type as owning-single / owning-collection / inverse-single by shape.</summary>
    private static bool TryClassifyRef(
        ITypeSymbol type,
        out RefKind kind,
        out string targetFqn,
        out string targetDisplay)
    {
        kind = RefKind.OwningSingle;
        targetFqn = "";
        targetDisplay = "";

        // Strip Nullable<T> annotation.
        if (type is INamedTypeSymbol named)
        {
            // EntityRef<T>?
            if (named.Name == "EntityRef" && named.ContainingNamespace.ToDisplayString() == "Forge.Entity"
                && named.TypeArguments.Length == 1)
            {
                var t = named.TypeArguments[0];
                targetFqn = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                targetDisplay = t.ToDisplayString();
                kind = RefKind.OwningSingle; // also used for inverse — caller distinguishes by attribute
                return true;
            }

            // EntityRefCollection<T>
            if (named.Name == "EntityRefCollection" && named.ContainingNamespace.ToDisplayString() == "Forge.Entity"
                && named.TypeArguments.Length == 1)
            {
                var t = named.TypeArguments[0];
                targetFqn = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                targetDisplay = t.ToDisplayString();
                kind = RefKind.OwningCollection;
                return true;
            }
        }
        return false;
    }

    private static bool IsAllowedIdentityPartType(ITypeSymbol t)
    {
        // Strip nullable annotation.
        if (t is INamedTypeSymbol nn && nn.IsGenericType && nn.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
            t = nn.TypeArguments[0];

        // Allow List<T> / IReadOnlyList<T> / IEnumerable<T> where T is an allowed scalar.
        if (t is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
        {
            var outerFqn = named.ConstructedFrom.ToDisplayString();
            if (outerFqn is "System.Collections.Generic.List<T>"
                         or "System.Collections.Generic.IReadOnlyList<T>"
                         or "System.Collections.Generic.IEnumerable<T>")
                return IsAllowedScalar(named.TypeArguments[0]);
        }

        return IsAllowedScalar(t);
    }

    private static bool IsAllowedScalar(ITypeSymbol t)
    {
        // Strip nullable annotation.
        if (t is INamedTypeSymbol nn && nn.IsGenericType && nn.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
            t = nn.TypeArguments[0];

        switch (t.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Boolean:
            case SpecialType.System_Int16:
            case SpecialType.System_Int32:
            case SpecialType.System_Int64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return true;
        }

        var fqn = t.ToDisplayString();
        return fqn is "System.Guid" or "System.DateTime" or "System.DateTimeOffset"
            or "System.DateOnly" or "System.TimeOnly" or "System.Uri";
    }
}
