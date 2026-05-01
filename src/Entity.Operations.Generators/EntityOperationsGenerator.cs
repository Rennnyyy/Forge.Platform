using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Forge.Entity.Operations.Generators;

/// <summary>
/// Incremental generator that emits active-record CRUD methods onto every
/// <c>[Entity]</c>-annotated <c>partial class</c> in the consuming project.
/// Emitted file hint: <c>{Namespace}.{TypeName}.g.ops.cs</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EntityOperationsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entityCandidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "Forge.Entity.EntityAttribute",
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (gac, _) => (INamedTypeSymbol)gac.TargetSymbol);

        context.RegisterSourceOutput(entityCandidates, static (spc, symbol) =>
        {
            var ns = symbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : symbol.ContainingNamespace.ToDisplayString();
            var typeName = symbol.Name;
            var isSealed = symbol.IsSealed;

            var source = EntityOperationsEmitter.Emit(ns, typeName, isSealed);
            var hint = ns.Length == 0 ? $"{typeName}.g.ops.cs" : $"{ns}.{typeName}.g.ops.cs";
            spc.AddSource(hint, source);
        });
    }
}
