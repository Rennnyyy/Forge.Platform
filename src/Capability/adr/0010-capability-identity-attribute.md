# 0010 — `[Capability]` identity attribute and `CapabilityIdentity` value object

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

Capabilities need a stable, structured identity that is independent of C# type names.
Type names are refactorable (rename, move namespace) and therefore unsuitable as
infrastructure coordinates. A renamed handler class would silently break an existing
HTTP route or message-queue binding.

The identity must be the single, canonical name for a capability **across all transports**:
the HTTP layer converts it to a route path; a future messaging layer converts it to a
queue or topic name using its own separator convention. Neither layer should embed the
derivation rule inline — that would scatter transport-specific knowledge across slices.

Three design questions need firm answers before implementation:

1. **Attribute placement** — on the handler type or the command POCO?
2. **Character set** — which characters are legal in an identity segment?
3. **Verb / method hint** — should the attribute carry an optional `Method` property?
4. **Versioning** — encoded in the identity string or via a separate attribute?
5. **Missing attribute policy** — what happens at route registration time if no
   `[Capability]` attribute is found on a handler?

## Options

### Attribute placement

1. **On the handler type (`ICapabilityHandler<TCommand, TResponse>` implementation).**
   The handler is the unit of deployment; the transport identity belongs to the handler,
   not the command data-bag. Command POCOs remain plain data types.
2. On the command POCO.
   Con: the command is a cross-cutting DTO; one command type could theoretically be
   handled by multiple handler registrations with different identities. Putting the
   identity on the command conflates the message contract with the routing decision.

### Character set

1. **`[a-z0-9][a-z0-9-]*[a-z0-9]$|^[a-z0-9]` per segment** — lowercase letters,
   digits, and internal hyphens; no uppercase, no underscores; `.` is the sole separator.
   This mirrors DNS-label conventions and is safe as both a URL path segment and a
   message-queue topic segment.
2. Allow uppercase letters. Con: route path derivation would need a case-normalisation
   step; queue-naming conventions (SNS, AMQP) are case-sensitive in different ways.
3. Allow underscores. Con: underscores are sometimes used as namespace separators in
   queue topologies (e.g. `catalog_artists`) conflicting with the dot separator chosen here.

### Verb / method hint

1. **No `Method` property on the attribute.** The HTTP verb is always supplied at the
   `MapCapabilities()` call site (or derived by the route auto-discovery in trunk-03).
   The identity attribute is transport-agnostic and carries only the name, not HTTP specifics.
2. Optional `Method` property on the attribute.
   Con: bakes an HTTP concern into a layer that must remain transport-agnostic.
   A future messaging layer would ignore or misuse the property.

### Versioning

1. **Part of the identity string** — `catalog.artists.v2.create`.
   Follows the same dot-segment rules. No special attribute or argument.
2. Separate `Version` argument or attribute.
   Pro: tooling can parse the version explicitly. Con: adds API surface;
   equivalent information is already present in the identity string; a separate
   version fragment complicates `ToRoutePath()`.

### Missing attribute policy

1. **Throw at route-registration time** (in the HTTP / messaging transport slice) with
   a clear message naming the handler type. `Forge.Capability` itself does not enforce
   this — it is a transport-layer concern. The core slice provides only the attribute
   type and `CapabilityIdentity`; transport slices call `Type.GetCustomAttribute<CapabilityAttribute>()`
   and raise `InvalidOperationException` when the result is null.
2. Throw at DI registration (`AddCapabilityHandler`).
   Con: `AddCapabilityHandler` is called at startup before routes are mapped.
   Requiring the attribute at DI time would break any handler registered without it,
   even if that handler will never be exposed via HTTP. The attribute must be optional
   from the DI layer's perspective.

## Decision

- **Attribute placement**: Option 1 — on the handler type.
- **Character set**: Option 1 — `^[a-z0-9]([a-z0-9-]*[a-z0-9])?$` per segment,
  `.` as the sole separator.
- **Method hint**: Option 1 — no `Method` property; identity is transport-agnostic.
- **Versioning**: Option 1 — encoded in the identity string.
- **Missing attribute**: Option 1 — transport slice enforces presence; core slice does
  not.

### API surface introduced in `Forge.Capability`

```csharp
// src/Capability/CapabilityAttribute.cs
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CapabilityAttribute : Attribute
{
    public CapabilityAttribute(string identity);
    public CapabilityIdentity Identity { get; }
}

// src/Capability/CapabilityIdentity.cs
public sealed record CapabilityIdentity
{
    /// <summary>The raw dot-separated identity string.</summary>
    public string Value { get; }

    /// <summary>
    /// Constructs a validated identity. Throws <see cref="ArgumentException"/> if any
    /// segment violates the character-set rule.
    /// </summary>
    public CapabilityIdentity(string value);

    /// <summary>Converts the identity to an HTTP route path by replacing '.' with '/'.</summary>
    public string ToRoutePath();

    public override string ToString();
}
```

### Validation rules for `CapabilityIdentity`

- The value must not be null or empty.
- The value is split by `.`; it must contain at least one segment.
- Each segment must match `^[a-z0-9]([a-z0-9-]*[a-z0-9])?$`:
  - Minimum one character.
  - First and last character are a lowercase letter or digit.
  - Interior characters may also be a hyphen.
- Violation throws `ArgumentException` naming the offending segment.

## Consequences

- Handler types are the single source of truth for capability identity; renaming a
  command POCO never changes the transport coordinate.
- `ToRoutePath()` is the only place the dot-to-slash derivation rule is encoded;
  the HTTP slice calls it directly and does not embed the rule inline.
- Future transport slices (messaging) call `Value` or define their own conversion
  without touching `Forge.Capability`.
- `AddCapabilityHandler` does not require `[Capability]`; handlers without the
  attribute can exist (e.g. internal capabilities not exposed via any transport).
- Version changes require a new identity string segment; breaking changes can be
  introduced under `v2` without removing the old handler registration.
