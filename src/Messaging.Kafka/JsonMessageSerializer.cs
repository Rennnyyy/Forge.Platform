using System.Text.Json;

namespace Forge.Messaging.Kafka;

/// <summary>
/// Default JSON-based implementation of <see cref="Forge.Messaging.Abstractions.IMessageSerializer{T}"/>
/// and <see cref="Forge.Messaging.Abstractions.IMessageDeserializer{T}"/> using
/// <see cref="System.Text.Json.JsonSerializer"/>.
/// <para>
/// Registered by <see cref="DependencyInjection.ForgeMessagingKafkaServiceCollectionExtensions.AddForgeMessagingKafka"/>
/// as the default serializer pair. Applications can replace these registrations
/// with custom implementations for schema-registry-validated Avro or Protobuf payloads.
/// </para>
/// See root ADR-0020.
/// </summary>
internal sealed class JsonMessageSerializer<T> :
    Forge.Messaging.Abstractions.IMessageSerializer<T>,
    Forge.Messaging.Abstractions.IMessageDeserializer<T>
{
    private readonly JsonSerializerOptions _options;

    public JsonMessageSerializer(JsonSerializerOptions? options = null)
        => _options = options ?? JsonSerializerOptions.Default;

    public ReadOnlyMemory<byte> Serialize(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, _options);

    public T Deserialize(ReadOnlyMemory<byte> bytes)
        => JsonSerializer.Deserialize<T>(bytes.Span, _options)
           ?? throw new InvalidOperationException($"Deserialization returned null for type {typeof(T).Name}.");
}
