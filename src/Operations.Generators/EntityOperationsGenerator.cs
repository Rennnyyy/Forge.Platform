using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Forge.Operations.Generators;

/// <summary>
/// Incremental generator that emits active-record CRUD methods onto every
/// <c>[Entity]</c>-annotated <c>partial class</c> in the consuming project.
/// Emitted file hint: <c>{Namespace}.{TypeName}.g.ops.cs</c>.
/// </summary>
/// <remarks>
/// Types decorated with <c>[Forge.Operations.NoOperationsAttribute]</c> are
/// silently skipped: no <c>.g.ops.cs</c> file is emitted for them. The structural
/// generator (<c>Forge.Entity.Generators</c>) is unaffected.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class EntityOperationsGenerator : IIncrementalGenerator
{
    private const string NoOperationsAttributeFullName = "Forge.Operations.NoOperationsAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entityCandidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "Forge.Entity.EntityAttribute",
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (gac, _) =>
            {
                var symbol = (INamedTypeSymbol)gac.TargetSymbol;
                var hasNoOps = false;
                foreach (var attr in symbol.GetAttributes())
                {
                    if (attr.AttributeClass?.ToDisplayString() == NoOperationsAttributeFullName)
                    {
                        hasNoOps = true;
                        break;
                    }
                }

                // Detect entity subtypes: base type also carries [Entity].
                var isSubtype = false;
                var baseType = symbol.BaseType;
                while (baseType is not null && baseType.SpecialType == SpecialType.None)
                {
                    foreach (var attr in baseType.GetAttributes())
                    {
                        if (attr.AttributeClass?.ToDisplayString() == "Forge.Entity.EntityAttribute")
                        {
                            isSubtype = true;
                            break;
                        }
                    }
                    if (isSubtype) break;
                    baseType = baseType.BaseType;
                }

                return (
                    Ns: symbol.ContainingNamespace.IsGlobalNamespace
                        ? ""
                        : symbol.ContainingNamespace.ToDisplayString(),
                    TypeName: symbol.Name,
                    IsSealed: symbol.IsSealed,
                    IsSubtype: isSubtype,
                    Skip: hasNoOps
                );
            });

        context.RegisterSourceOutput(entityCandidates, static (spc, model) =>
        {
            if (model.Skip) return;

            var source = EntityOperationsEmitter.Emit(model.Ns, model.TypeName, model.IsSealed, model.IsSubtype);
            var hint = model.Ns.Length == 0
                ? $"{model.TypeName}.g.ops.cs"
                : $"{model.Ns}.{model.TypeName}.g.ops.cs";
            spc.AddSource(hint, source);
        });
    }
}
