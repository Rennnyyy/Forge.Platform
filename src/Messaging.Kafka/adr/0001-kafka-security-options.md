# ADR-0001 — First-class TLS/SASL security properties

**Status:** Accepted  
**Date:** 2025-07-14  
**Slice:** `Forge.Messaging.Kafka`

## Context

`KafkaProducerOptions` and `KafkaConsumerOptions` expose an `AdditionalConfig`
dictionary for pass-through Confluent.Kafka key/value settings. Production
deployments that require TLS (`ssl.endpoint.identification.algorithm`) or SASL
authentication (`sasl.mechanism`, `sasl.username`, `sasl.password`) must populate
this dictionary with untyped string keys — a process that is:

- error-prone (misspelled keys fail silently at connection time),
- not visible in IDE tooling or configuration documentation, and
- insecure (passwords stored alongside arbitrary key/value strings with no
  differentiation in terms of sensitivity).

Confluent.Kafka 2.x surfaces these settings as strongly-typed properties on
`ClientConfig` (`SecurityProtocol`, `SaslMechanism`, `SaslUsername`,
`SaslPassword`). There is no reason to hide them behind the escape-hatch dict.

## Decision

A new `KafkaSecurityOptions` class is added to `Forge.Messaging.Kafka`. It groups
the four most common transport-security properties that must be set together:

| Property | Confluent.Kafka type | Role |
|---|---|---|
| `SecurityProtocol` | `Confluent.Kafka.SecurityProtocol?` | Transport layer (`Plaintext`, `Ssl`, `SaslPlaintext`, `SaslSsl`) |
| `SaslMechanism` | `Confluent.Kafka.SaslMechanism?` | Auth protocol (`Plain`, `ScramSha256`, `ScramSha512`, `Gssapi`, `OAuthBearer`) |
| `SaslUsername` | `string?` | Credential identity |
| `SaslPassword` | `string?` | Credential secret |

Both `KafkaProducerOptions` and `KafkaConsumerOptions` gain an optional
`KafkaSecurityOptions? Security` property. `ToConfluentConfig()` in each class
maps only the non-null fields onto the underlying `ClientConfig` object. Null
fields are left unset, preserving backward compatibility: existing deployments that
supply security settings through `AdditionalConfig` continue to work.

## Consequences

**Positive**
- Transport-security intent is explicit and type-safe in POCO/JSON configuration.
- Operators get compile-time validation and IDE completion for the most sensitive
  Kafka settings.
- Reduces the risk of misconfigured production clusters silently running without
  TLS due to a typo in `AdditionalConfig`.

**Neutral**
- `AdditionalConfig` remains available for any advanced properties not yet promoted
  to first-class fields; it continues to be merged last (highest precedence) so
  it can override structured properties if necessary.

**Negative**
- The slice now owns a stable public type (`KafkaSecurityOptions`) whose property
  set must be extended before new security mechanisms added by future Confluent.Kafka
  versions (e.g., delegation-token auth) can be set in a first-class way.
