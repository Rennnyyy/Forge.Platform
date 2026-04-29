using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Forge.Entity.Generators;

/// <summary>
/// Incremental generator that emits the second partial half for every
/// <c>[Entity]</c>-annotated <c>partial class</c> in the consuming project.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EntityGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entityCandidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "Forge.Entity.EntityAttribute",
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (gac, _) => (
                Symbol: (INamedTypeSymbol)gac.TargetSymbol,
                Decl: (ClassDeclarationSyntax)gac.TargetNode));

        context.RegisterSourceOutput(entityCandidates.Collect(), static (spc, batch) =>
        {
            // Phase 1: parse all candidates into models so cross-entity inverse lookup works.
            var models = new List<EntityModel>();
            foreach (var (sym, decl) in batch)
            {
                var model = EntityParser.TryParse(sym, decl, spc);
                if (model is not null) models.Add(model);
            }
            var registry = new EntityRegistry(models);

            // Phase 2: emit per type.
            foreach (var model in models)
            {
                var source = EntityEmitter.Emit(model, registry);
                spc.AddSource(model.FileName, source);
            }
        });
    }
}
