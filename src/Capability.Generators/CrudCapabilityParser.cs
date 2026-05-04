using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Forge.Capability.Generators;

internal static class CrudCapabilityParser
{
    private const string EntityAttr          = "Forge.Entity.EntityAttribute";
    private const string CrudCapabilitiesAttr = "Forge.Capability.CrudCapabilitiesAttribute";
    private const string IdentityPartAttr    = "Forge.Entity.IdentityPartAttribute";
    private const string PredicateAttr       = "Forge.Entity.PredicateAttribute";
    private const string OwningAttr          = "Forge.Entity.OwningAttribute";
    private const string InverseAttr         = "Forge.Entity.InverseAttribute";

    public static CrudCapabilityEntityModel? TryParse(INamedTypeSymbol symbol)
    {
        // Locate [CrudCapabilities] — the generator entry point
        var crudAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudCapabilitiesAttr);
        if (crudAttr is null) return null;

        // Read CrudMethod flags; default = All (31) when argument is omitted
        var methodsValue = crudAttr.ConstructorArguments.Length > 0
            ? System.Convert.ToInt32(crudAttr.ConstructorArguments[0].Value)
            : 31;

        // Locate [Entity] for the capability path segment
        var entityAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EntityAttr);

        var entityPath = entityAttr?.NamedArguments
            .FirstOrDefault(kv => kv.Key == "Path").Value.Value as string;
        var capabilityPathSegment = entityPath ?? symbol.Name.ToLowerInvariant();

        // Collect identity parts and predicate data properties
        var identityPropsList = new List<PropModel>();
        var dataPropsList = new List<PropModel>();

        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            // Skip entity-ref properties ([Owning] / [Inverse])
            if (member.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == OwningAttr ||
                    a.AttributeClass?.ToDisplayString() == InverseAttr))
                continue;

            var attrs = member.GetAttributes();
            var isIdentityPart = attrs.Any(a => a.AttributeClass?.ToDisplayString() == IdentityPartAttr);
            var hasPredicate   = attrs.Any(a => a.AttributeClass?.ToDisplayString() == PredicateAttr);

            // Only capture properties explicitly marked for persistence
            if (!isIdentityPart && !hasPredicate) continue;

            // A property with no set or init accessor is read-only computed — skip
            if (member.SetMethod is null) continue;

            var isInitOnly  = member.SetMethod.IsInitOnly;
            var typeDisplay = member.Type.ToDisplayString();

            if (isIdentityPart)
            {
                var partAttr = attrs.First(a => a.AttributeClass?.ToDisplayString() == IdentityPartAttr);
                var order = System.Convert.ToInt32(
                    partAttr.ConstructorArguments.Length > 0 ? partAttr.ConstructorArguments[0].Value : 0);
                identityPropsList.Add(new PropModel(member.Name, typeDisplay, isInitOnly, order));
            }
            else
            {
                dataPropsList.Add(new PropModel(member.Name, typeDisplay, isInitOnly));
            }
        }

        // Sort identity parts by their declared order
        identityPropsList.Sort((a, b) => a.Order.CompareTo(b.Order));

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();

        var fqn = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new CrudCapabilityEntityModel(
            FullyQualifiedName: fqn,
            Namespace: ns,
            TypeName: symbol.Name,
            CapabilityPathSegment: capabilityPathSegment,
            Methods: methodsValue,
            IdentityProps: ImmutableArray.CreateRange(identityPropsList),
            DataProps: ImmutableArray.CreateRange(dataPropsList));
    }
}
