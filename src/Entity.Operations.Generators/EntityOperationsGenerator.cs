using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Forge.Entity.Operations.Generators;

/// <summary>
/// Incremental generator that emits active-record CRUD methods onto every
/// <c>[Entity]</c>-annotated <c>partial class</c> in the consuming project.
/// Emitted file hint: <c>{Namespace}.{TypeName}.g.ops.cs</c>.
/// </summary>
/// <remarks>
/// Types decorated with <c>[Forge.Entity.Operations.NoOperationsAttribute]</c> are
/// silently skipped: no <c>.g.ops.cs</c> file is emitted for them. The structural
/// generator (<c>Forge.Entity.Generators</c>) is unaffected.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class EntityOperationsGenerator : IIncrementalGenerator
{
    private const string NoOperationsAttributeFullName = "Forge.Entity.Operations.NoOperationsAttribute";

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

                return (
                    Ns: symbol.ContainingNamespace.IsGlobalNamespace
                        ? ""
                        : symbol.ContainingNamespace.ToDisplayString(),
                    TypeName: symbol.Name,
                    IsSealed: symbol.IsSealed,
                    Skip: hasNoOps
                );
            });

        context.RegisterSourceOutput(entityCandidates, static (spc, model) =>
        {
            if (model.Skip) return;

            var source = EntityOperationsEmitter.Emit(model.Ns, model.TypeName, model.IsSealed);
            var hint = model.Ns.Length == 0
                ? $"{model.TypeName}.g.ops.cs"
                : $"{model.Ns}.{model.TypeName}.g.ops.cs";
            spc.AddSource(hint, source);
        });
    }
}
