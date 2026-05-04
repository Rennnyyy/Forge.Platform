using System.Collections.Immutable;
using Forge.Capability;
using Forge.Capability.Generators;
using Forge.Entity;
using Forge.Entity.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Forge.Capability.Generators.Tests;

/// <summary>
/// Drives the <see cref="CrudCapabilityGenerator"/> (and <see cref="EntityGenerator"/>
/// for entity partial class support) over arbitrary C# source and exposes the emitted
/// files and diagnostics for assertion-based testing.
/// </summary>
internal static class CrudCapabilityGeneratorRunner
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Latest);

    public sealed record RunResult(
        ImmutableArray<(string FileName, string Source)> EmittedFiles,
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableArray<Diagnostic> CompilationDiagnostics);

    public static RunResult Run(string source, params string[] additionalSources)
    {
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source, ParseOptions) };
        foreach (var s in additionalSources)
            trees.Add(CSharpSyntaxTree.ParseText(s, ParseOptions));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Forge.Entity — for [Entity], [IdentityPart], [Predicate] attribute symbols
        references.Add(MetadataReference.CreateFromFile(typeof(EntityAttribute).Assembly.Location));
        // Forge.Capability — for [CrudCapabilities], ICapabilityHandler, CapabilityResult, etc.
        references.Add(MetadataReference.CreateFromFile(typeof(CrudCapabilitiesAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        // Run EntityGenerator first so partial class members are available to subsequent generators
        var entityDriver = CSharpGeneratorDriver.Create(new EntityGenerator());
        entityDriver = (CSharpGeneratorDriver)entityDriver.RunGeneratorsAndUpdateCompilation(
            compilation, out var compilationWithEntity, out _);

        // Run CrudCapabilityGenerator on the enriched compilation
        var crudDriver = CSharpGeneratorDriver.Create(new CrudCapabilityGenerator());
        crudDriver = (CSharpGeneratorDriver)crudDriver.RunGeneratorsAndUpdateCompilation(
            compilationWithEntity, out var finalCompilation, out _);

        var runResult = crudDriver.GetRunResult();

        var emittedFiles = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(gs => (gs.HintName, gs.SourceText.ToString()))
            .ToImmutableArray();

        var generatorDiagnostics = runResult.Diagnostics;

        var compilationDiagnostics = finalCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        return new RunResult(emittedFiles, generatorDiagnostics, compilationDiagnostics);
    }
}
