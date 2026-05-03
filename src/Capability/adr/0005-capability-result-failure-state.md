# 0005 — `CapabilityResult<TResponse>` carries a failure state

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent (pending user acceptance)

## Context

ADR-0002 defined `CapabilityResult<TResponse>` as success-only:

```csharp
public sealed record CapabilityResult<TResponse> where TResponse : class
{
    public required TResponse Response { get; init; }
    public IReadOnlyList<object> Events { get; init; } = [];
}
```

A handler has no way to signal a non-exceptional failure (validation rejection, business
rule violation, resource not found) without throwing an exception. Exception-as-flow-control
is undesirable in async pipelines: it is expensive, breaks structured result inspection, and
makes test assertions awkward.

Three pressures:

1. Handlers must be able to return structured error information (a code + human-readable
   message) without throwing.
2. Callers (the future dispatcher and any test) must not be able to silently access
   `Response` when the result is a failure.
3. The solution should compose naturally with C# pattern matching, which is the idiomatic
   way to consume discriminated unions in .NET.

## Options

### Option 1 — Nested `Ok` / `Fail` records (discriminated union)

```csharp
public abstract record CapabilityResult<TResponse> where TResponse : class
{
    public IReadOnlyList<object> Events { get; init; } = [];

    public sealed record Ok(TResponse Response) : CapabilityResult<TResponse>;
    public sealed record Fail(CapabilityError Error) : CapabilityResult<TResponse>;
}

public sealed record CapabilityError(string Code, string Message);
```

Usage:

```csharp
return new CapabilityResult<MyResponse>.Ok(new MyResponse(...));
return new CapabilityResult<MyResponse>.Fail(new CapabilityError("NOT_FOUND", "Artist not found"));

// caller:
result switch
{
    CapabilityResult<MyResponse>.Ok ok   => ok.Response,
    CapabilityResult<MyResponse>.Fail fail => throw new ...(fail.Error.Message),
}
```

Pro: type-safe — accidental access of `Response` on a failure is a compile error.
Pro: pattern matching is idiomatic and exhaustiveness is compiler-checked.
Con: slightly more verbose construction; nested type name is longer.

### Option 2 — `IsSuccess` flag + nullable properties on a single record

```csharp
public sealed record CapabilityResult<TResponse> where TResponse : class
{
    public bool IsSuccess { get; private init; }
    public TResponse? Response { get; private init; }   // non-null iff IsSuccess
    public CapabilityError? Error { get; private init; } // non-null iff !IsSuccess
    public IReadOnlyList<object> Events { get; init; } = [];

    public static CapabilityResult<TResponse> Ok(TResponse response, ...) => ...;
    public static CapabilityResult<TResponse> Fail(CapabilityError error) => ...;
}
```

Pro: single type, familiar (`HttpResponseMessage`, `Result<T>` pattern).
Con: `Response` is nullable; callers can still access it without checking `IsSuccess` —
  the compiler cannot enforce correct usage without nullable-analysis warnings.
Con: the `required` keyword on `Response` from ADR-0002 must be dropped; no static contract
  distinguishes the two states.

### Option 3 — Keep success-only; use exceptions for failures

No change to `CapabilityResult<TResponse>`. Handlers throw domain exceptions
(e.g. `CapabilityException`, `NotFoundException`) for failure paths.

Pro: zero contract change.
Con: exception-as-flow-control; expensive for expected failure paths; hard to unit-test
  without `try/catch` and fragile type checks.

## Decision

**Option 1** — nested `Ok` / `Fail` records.

The compile-time safety guarantee (no accidental `Response` access on failure) justifies
the slightly longer construction syntax. Pattern matching exhaustiveness fits the existing
Capability dispatcher pipeline described in ADR-0002.

`CapabilityError` is a plain value record (Code + Message) introduced alongside the change.
The `Events` collection is hoisted to the abstract base so it is available on `Ok` results
and absent (empty) on `Fail` results by default.

## Changes

| Action | File |
|--------|------|
| Modify | `src/Capability/CapabilityResult.cs` — replace with abstract record + nested Ok/Fail |
| Create | `src/Capability/CapabilityError.cs` |
| Modify | `tests/Capability.Tests/CapabilityTests.cs` — update all result construction and assertions |
| Adjust | ADR-0002 — inline note: `CapabilityResult<TResponse>` is now a discriminated union; see ADR-0005 |

## Consequences

- Handlers always construct results via `new CapabilityResult<T>.Ok(...)` or
  `new CapabilityResult<T>.Fail(...)`.
- The future dispatcher uses `switch` / pattern matching on the result shape.
- A caller that forgets to handle the `Fail` case will receive a compiler warning
  (non-exhaustive switch) or a `MatchFailureException` at runtime, making bugs surface early.
- Tests can assert on the concrete nested type directly, keeping assertions readable.
