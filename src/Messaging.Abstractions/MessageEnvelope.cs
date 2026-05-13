using Forge.Execution;

namespace Forge.Messaging.Abstractions;

/// <summary>
/// Broker-agnostic message envelope. Carries the payload together with the
/// routing, correlation, and schema metadata required by all platform messaging.
/// See root ADR-0020.
/// </summary>
/// <typeparam name="TValue">The payload type.</typeparam>
/// <param name="Topic">The logical topic name this envelope is destined for.</param>
/// <param name="PartitionKey">Key used by the broker to determine the partition. Always a string in platform use.</param>
/// <param name="Payload">The message payload.</param>
/// <param name="Correlation">Execution correlation identifiers propagated from the originating dispatch.</param>
/// <param name="TimestampUtc">UTC timestamp at which the envelope was created.</param>
/// <param name="ContentType">MIME content-type of the serialized payload bytes. Defaults to <c>application/json</c>.</param>
/// <param name="SchemaVersion">Monotonically increasing schema version. Increment when making breaking payload shape changes.</param>
public sealed record MessageEnvelope<TValue>(
    string Topic,
    string PartitionKey,
    TValue Payload,
    ExecutionCorrelation Correlation,
    DateTimeOffset TimestampUtc,
    string ContentType = "application/json",
    int SchemaVersion = 1);
