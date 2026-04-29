using System.Collections.Immutable;
using System.Reflection;
using Forge.Entity.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Forge.Entity.Generators.Tests;

/// <summary>
/// Drives the source generator over arbitrary C# input and exposes the
/// emitted source files plus diagnostics for assertion-based testing.
/// </summary>
internal static class GeneratorRunner
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
        foreach (var s in additionalSources) trees.Add(CSharpSyntaxTree.ParseText(s, ParseOptions));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();
        // Make sure Forge.Entity is referenced.
        references.Add(MetadataReference.CreateFromFile(typeof(EntityAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new EntityGenerator()).WithUpdatedParseOptions(ParseOptions);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var updated, out var diagnostics);

        var runResult = driver.GetRunResult();
        var emitted = runResult.Results
            .SelectMany(r => r.GeneratedSources.Select(s => (s.HintName, s.SourceText.ToString())))
            .ToImmutableArray();

        var compilationDiags = updated.GetDiagnostics();
        return new RunResult(emitted, diagnostics, compilationDiags);
    }
}
