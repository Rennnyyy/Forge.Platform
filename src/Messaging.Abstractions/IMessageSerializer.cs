namespace Forge.Messaging.Abstractions;

/// <summary>
/// Serializes a value of type <typeparamref name="T"/> to raw bytes for broker transport.
/// Implemented by feature slices (e.g. <c>Forge.Entity.Messaging</c>) using System.Text.Json.
/// See root ADR-0020.
/// </summary>
/// <typeparam name="T">The value type to serialize.</typeparam>
public interface IMessageSerializer<T>
{
    /// <summary>Serializes <paramref name="value"/> to bytes.</summary>
    ReadOnlyMemory<byte> Serialize(T value);
}

/// <summary>
/// Deserializes raw bytes from broker transport back into a value of type <typeparamref name="T"/>.
/// Implemented by feature slices using System.Text.Json.
/// See root ADR-0020.
/// </summary>
/// <typeparam name="T">The value type to deserialize.</typeparam>
public interface IMessageDeserializer<T>
{
    /// <summary>Deserializes <paramref name="bytes"/> to a value of type <typeparamref name="T"/>.</summary>
    T Deserialize(ReadOnlyMemory<byte> bytes);
}
