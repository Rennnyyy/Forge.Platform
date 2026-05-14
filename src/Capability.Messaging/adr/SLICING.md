# Capability.Messaging — slice layout

**Root ADR**: [0022](../../../adr/0022-async-capability-command-bus.md)

## Flat-layout exemption

The slice currently contains 11 `.cs` files at project root (ADR-0010 threshold for
sub-folder groupings).  After evaluation no sub-folder grouping with a clear, stable
boundary emerged:

| File | Role |
|------|------|
| `IAsyncCapabilityDispatcher.cs` | Public dispatching contract |
| `AsyncCapabilityDispatcher.cs` | Implementation; publishes command + awaits reply |
| `CapabilityMessagingOptions.cs` | Configuration: consumer group, request timeout |
| `PendingReplyRegistry.cs` | In-process TCS map (internal) |
| `ICapabilityMessageConsumer.cs` | Public consumption contract |
| `CapabilityMessageConsumer.cs` | Consumer implementation (internal) |
| `CapabilityCommandPumpService.cs` | Hosted pump: routes inbound commands (internal) |
| `CapabilityReplyPumpService.cs` | Hosted pump: routes inbound replies (internal) |
| `CapabilityReplyListener.cs` | Completes pending TCS on reply receipt (internal) |
| `CapabilityCommandEnvelope.cs` | Envelope record wrapping capability command (internal) |
| `CapabilityReplyEnvelope.cs` | Envelope record wrapping capability reply (internal) |

A "Dispatch / Consume / Pump / Envelope" split was considered. The boundary is not stable:
`AsyncCapabilityDispatcher` both dispatches *and* awaits a reply from`PendingReplyRegistry`,
making the dispatch group inseparable from the registry. The pump services and listener are
purely internal plumbing with no public surface. Moving them into sub-folders would produce
one small public group (`IAsyncCapabilityDispatcher`, `ICapabilityMessageConsumer`,
envelope records) and one large internal group with no cohesive name.

The flat layout is retained pending a future re-evaluation if the file count grows past ~15.

## DependencyInjection sub-folder

`DependencyInjection/` holds the slice's DI extension class
(`CapabilityMessagingServiceCollectionExtensions.cs`) and is excluded from the flat-file
count per ADR-0010 convention.
