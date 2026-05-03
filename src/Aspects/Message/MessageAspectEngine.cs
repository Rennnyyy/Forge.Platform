using System.Reflection;
using Forge.Aspects.Shape;
using VDS.RDF;
using VDS.RDF.Shacl;

namespace Forge.Aspects.Message;

/// <summary>
/// Default <see cref="IMessageAspectEngine"/>. Projects a message object into a local RDF graph
/// via reflection and evaluates the aspect's ShapeTtl via ShapesGraph.Validate.
/// Single-pass (Local only) — no Context SPARQL pass, no store access.
/// See Capability ADR-0001 and Aspects ADR-0001.
/// </summary>
internal sealed class MessageAspectEngine : IMessageAspectEngine
{
    private static readonly string ShaclViolationIri = "http://www.w3.org/ns/shacl#Violation";
    private static readonly Uri RdfTypeUri = UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type");
    private static readonly string XsdNs = "http://www.w3.org/2001/XMLSchema#";
    private static readonly string ForgeNs = "https://forge-it.net/";

    private readonly IShapeCache _cache;

    public MessageAspectEngine(IShapeCache cache)
    {
        _cache = cache;
    }

    public ValueTask ValidateAsync(
        object message,
        IMessageAspect? aspect,
        CancellationToken cancellationToken = default)
    {
        if (aspect?.ShapeTtl is not { } shapeTtl)
            return ValueTask.CompletedTask;

        var messageGraph = BuildMessageGraph(message);
        var shapesGraph = _cache.GetOrParse(shapeTtl);
        var report = shapesGraph.Validate(messageGraph);

        if (report.Conforms)
            return ValueTask.CompletedTask;

        var violations = report.Results
            .Where(r => r.Severity?.ToString() == ShaclViolationIri ||
                        r.Severity?.ToString().EndsWith("#Violation", StringComparison.Ordinal) == true)
            .Select(r => new AspectViolation(
                FocusNodeIri: r.FocusNode?.ToString() ?? SubjectIri(message.GetType()),
                PathPredicate: r.ResultPath?.ToString(),
                Severity: r.Severity?.ToString() ?? ShaclViolationIri,
                Message: r.Message?.ToString() ?? "Constraint violated.",
                SourceShapeIri: r.SourceShape?.ToString()))
            .ToList();

        if (violations.Count > 0)
            throw new MessageAspectViolationException(message.GetType(), aspect.Name, violations);

        return ValueTask.CompletedTask;
    }

    // ------------------------------------------------------------------ Graph projection

    private static Graph BuildMessageGraph(object message)
    {
        var type = message.GetType();
        var graph = new Graph();
        var subject = graph.CreateUriNode(UriFactory.Create(SubjectIri(type)));

        // Assert rdf:type so sh:targetClass constraints work in SHACL shapes.
        graph.Assert(
            subject,
            graph.CreateUriNode(RdfTypeUri),
            graph.CreateUriNode(UriFactory.Create(ClassIri(type))));

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;

            var value = prop.GetValue(message);
            if (value is null) continue;

            var underlying = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            var predicate = graph.CreateUriNode(UriFactory.Create(ForgeNs + prop.Name));

            if (underlying == typeof(Uri))
            {
                graph.Assert(subject, predicate, graph.CreateUriNode((Uri)value));
            }
            else if (XsdSuffix(underlying) is { } suffix)
            {
                graph.Assert(subject, predicate,
                    graph.CreateLiteralNode(FormatLiteral(underlying, value),
                        UriFactory.Create(XsdNs + suffix)));
            }
            // Collections and unsupported types are silently skipped (v1).
        }

        return graph;
    }

    private static string SubjectIri(Type t) => $"urn:{t.FullName}:instance";
    private static string ClassIri(Type t) => $"urn:{t.FullName}";

    private static string? XsdSuffix(Type t)
    {
        if (t == typeof(string)) return "string";
        if (t == typeof(bool)) return "boolean";
        if (t == typeof(int)) return "int";
        if (t == typeof(long)) return "long";
        if (t == typeof(float)) return "float";
        if (t == typeof(double)) return "double";
        if (t == typeof(decimal)) return "decimal";
        if (t == typeof(DateOnly)) return "date";
        if (t == typeof(DateTimeOffset)) return "dateTime";
        if (t == typeof(Guid)) return "string";
        return null;
    }

    private static string FormatLiteral(Type t, object value)
    {
        if (t == typeof(bool)) return (bool)value ? "true" : "false";
        if (t == typeof(DateOnly)) return ((DateOnly)value).ToString("yyyy-MM-dd");
        if (t == typeof(DateTimeOffset)) return ((DateTimeOffset)value).ToString("o");
        return value.ToString()!;
    }
}
