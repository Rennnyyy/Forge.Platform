# Architecture Decision Records — Forge.Capability

Slice-local decisions for the Capability library. Read after the [root ADRs](../../../adr/) and the [Aspects slice ADRs](../../Aspects/adr/).

Format and rules: see [root ADR README](../../../adr/README.md).

## Index

- [0001 — `IMessageAspect` as the third aspect leg](0001-imessageaspect-third-leg.md)
- [0002 — `CapabilityContext` carries resolved aspects to the handler](0002-capability-context.md)
- [0003 — `[Command]`, `[Response]`, `[Event]` drive SHACL generation](0003-message-attributes-drive-generation.md)
- [0004 — No transaction / query scope in v1](0004-no-transaction-query-scope.md)
- [0005 — `CapabilityResult<TResponse>` carries a failure state](0005-capability-result-failure-state.md)
- [0006 — `ICapabilityDispatcher<TCommand,TResponse>`: dispatcher pipeline implementation](0006-capability-dispatcher-design.md)
- [0007 — Shapes provided dynamically per dispatch call; no generator](0007-dynamic-per-call-aspects.md)
- [0008 — Dispatcher reads ambient agent token into `CapabilityContext`](0008-agent-token-in-capability-context.md)
- [0009 — Rename `ICapabilityAspectGuard` → `IAspectGuard`; drop `MessageKind` parameter](0009-rename-iaspectguard-drop-messagekind.md)
- [0010 — `[Capability]` identity attribute and `CapabilityIdentity` value object](0010-capability-identity-attribute.md)
- [0011 — Assembly-scanning registration: `AddCapabilityHandlersFromAssemblyContaining<T>`](0011-assembly-scanning-registration.md)
- [0011 — Assembly-scan autodiscovery for capability handler registration](0011-capability-handler-autodiscovery.md)
- [0012 — `Forge.Capability.Generators`: CRUD capability generator](0012-crud-capability-generator.md)
- [0013 — `[CrudCapabilityHandler]` marker attribute on generated handlers](0013-crud-handler-marker-attribute.md)
