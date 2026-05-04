using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Forge.Capability.Generators;

/// <summary>
/// Incremental generator that emits CRUD capability handlers for every
/// <c>[Entity]</c>-annotated <c>[CrudCapabilities]</c>-decorated class.
/// Emitted file hint: <c>{Namespace}.{TypeName}.g.caps.cs</c>.
/// </summary>
/// <remarks>
/// The generator reads <c>[CrudCapabilities(CrudMethod)]</c> to determine which of the
/// five operations (Create, Read, Update, Delete, List) to generate. For each requested
/// operation it emits a command record, a response record, and an
/// <c>ICapabilityHandler&lt;TCommand, TResponse&gt;</c> implementation that delegates to
/// the entity's active-record methods produced by <c>Forge.Operations.Generators</c>.
/// </remarks>
/// See Capability ADR-0012.
[Generator(LanguageNames.CSharp)]
public sealed class CrudCapabilityGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "Forge.Capability.CrudCapabilitiesAttribute",
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (gac, _) =>
                CrudCapabilityParser.TryParse((INamedTypeSymbol)gac.TargetSymbol));

        context.RegisterSourceOutput(candidates, static (spc, model) =>
        {
            if (model is null) return;
            spc.AddSource(model.FileName, CrudCapabilityEmitter.Emit(model));
        });
    }
}
