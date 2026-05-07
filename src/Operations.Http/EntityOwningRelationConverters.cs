using System.Text.Json;
using System.Text.Json.Serialization;
using Forge.Entity;

namespace Forge.Operations.Http;

/// <summary>
/// <see cref="JsonConverterFactory"/> that handles <c>EntityRef&lt;T&gt;</c> types for the
/// Operations.Http entity endpoints. Serializes as a plain JSON string (the IRI).
/// <para>
/// Registered by <c>AddOperationEndpointsHttp*()</c> on the ASP.NET HTTP JSON options so that
/// <c>GET api/entities/{path}</c> and <c>GET api/entities/{path}?iri=</c> serialize
/// N:1 <c>[Owning]</c> properties as IRI strings alongside the scalar fields.
/// </para>
/// </summary>
internal sealed class EntityRefJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        var t = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(EntityRef<>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var t = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        var target = t.GetGenericArguments()[0];
        var converterType = typeof(EntityRefConverter<>).MakeGenericType(target);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// Serializes an <c>EntityRef&lt;T&gt;</c> (or null) as a plain JSON IRI string.
/// </summary>
internal sealed class EntityRefConverter<T> : JsonConverter<EntityRef<T>?>
    where T : class, IEntity
{
    public override EntityRef<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var iri = reader.GetString();
        return string.IsNullOrWhiteSpace(iri) ? null : EntityRef<T>.ForIri(iri);
    }

    public override void Write(Utf8JsonWriter writer, EntityRef<T>? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Iri);
    }
}

/// <summary>
/// <see cref="JsonConverterFactory"/> that handles <c>EntityRefCollection&lt;T&gt;</c> types.
/// Serializes as a JSON array of IRI strings (from <c>collection.Iris</c>).
/// </summary>
internal sealed class EntityRefCollectionJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        var t = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(EntityRefCollection<>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var t = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        var target = t.GetGenericArguments()[0];
        var converterType = typeof(EntityRefCollectionConverter<>).MakeGenericType(target);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// Serializes an <c>EntityRefCollection&lt;T&gt;</c> as a JSON array of IRI strings.
/// </summary>
internal sealed class EntityRefCollectionConverter<T> : JsonConverter<EntityRefCollection<T>>
    where T : class, IEntity
{
    public override EntityRefCollection<T>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        // The binder handles collection population from the request body directly;
        // this converter is only needed for response serialization from GET endpoints.
        => null;

    public override void Write(
        Utf8JsonWriter writer, EntityRefCollection<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var iri in value.Iris)
            writer.WriteStringValue(iri);
        writer.WriteEndArray();
    }
}
