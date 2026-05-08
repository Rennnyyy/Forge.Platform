using Forge.Capability;
using Forge.Execution;
using Forge.Capability.Http;

namespace Forge.Application.Sample;

// ── Shared nested POCO ─────────────────────────────────────────────────────────

/// <summary>Physical dimensions in centimetres.</summary>
public sealed record ItemDimensions(double WidthCm, double HeightCm, double DepthCm);

// ── CREATE  POST /demo/catalog/items/create ────────────────────────────────────

/// <summary>
/// Demonstrates every supported scalar CLR type, their nullable variants, an
/// <see cref="IReadOnlyList{T}"/> of strings and of <see cref="Guid"/>, and a
/// nested POCO (<see cref="ItemDimensions"/>).
/// </summary>
public sealed record CreateItemCommand(
    // ── Non-nullable scalars (one of each supported CLR type) ──
    string Name,
    bool IsAvailable,
    int Quantity,
    long Barcode,
    float WeightKg,
    double PriceEur,
    decimal TaxRate,
    DateOnly AvailableFrom,
    DateTimeOffset CreatedAt,
    Guid ExternalId,
    Uri ThumbnailUri,
    // ── Nullable variants ──
    string? Description,
    bool? IsFeatured,
    int? ReorderPoint,
    long? AlternateBarcode,
    float? DiscountPct,
    double? CompareAtPrice,
    decimal? ShippingCost,
    DateOnly? DiscontinuedOn,
    DateTimeOffset? LastModifiedAt,
    Guid? ParentItemId,
    Uri? CanonicalUri,
    // ── Collections ──
    IReadOnlyList<string> Tags,
    IReadOnlyList<Guid> CategoryIds,
    // ── Nested POCO ──
    ItemDimensions Dimensions);

public sealed record CreateItemResponse(Guid ItemId, DateTimeOffset StoredAt);

[Capability("demo.catalog.items.create")]
public sealed class CreateItemHandler : ICapabilityHandler<CreateItemCommand, CreateItemResponse>
{
    private readonly ItemStore _store;

    public CreateItemHandler(ItemStore store) => _store = store;

    public ValueTask<ExecutionResult<CreateItemResponse>> HandleAsync(
        CreateItemCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        var itemId = Guid.NewGuid();
        var storedAt = DateTimeOffset.UtcNow;

        _store.Save(new StoredItem(
            itemId,
            Name: command.Name,
            IsAvailable: command.IsAvailable,
            Quantity: command.Quantity,
            PriceEur: command.PriceEur,
            Tags: command.Tags,
            Dimensions: command.Dimensions,
            CreatedAt: storedAt));

        return ValueTask.FromResult<ExecutionResult<CreateItemResponse>>(
            new ExecutionResult<CreateItemResponse>.Ok(
                new CreateItemResponse(itemId, storedAt)));
    }
}

// ── UPDATE  PUT /demo/catalog/items/update ─────────────────────────────────────

public sealed record UpdateItemCommand(
    Guid Id,
    string Name,
    double PriceEur,
    int Quantity,
    bool IsAvailable,
    IReadOnlyList<string> Tags,
    ItemDimensions Dimensions);

public sealed record UpdateItemResponse(Guid Id, DateTimeOffset UpdatedAt);

[Capability("demo.catalog.items.update")]
[CapabilityEndpoint("PUT")]
public sealed class UpdateItemHandler : ICapabilityHandler<UpdateItemCommand, UpdateItemResponse>
{
    private readonly ItemStore _store;

    public UpdateItemHandler(ItemStore store) => _store = store;

    public ValueTask<ExecutionResult<UpdateItemResponse>> HandleAsync(
        UpdateItemCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        var existing = _store.TryGet(command.Id);
        if (existing is null)
            return ValueTask.FromResult<ExecutionResult<UpdateItemResponse>>(
                new ExecutionResult<UpdateItemResponse>.Fail(
                    new ExecutionError("ITEM_NOT_FOUND", $"No item with id '{command.Id}'.")));

        var updatedAt = DateTimeOffset.UtcNow;
        _store.Save(existing with
        {
            Name = command.Name,
            PriceEur = command.PriceEur,
            Quantity = command.Quantity,
            IsAvailable = command.IsAvailable,
            Tags = command.Tags,
            Dimensions = command.Dimensions,
        });

        return ValueTask.FromResult<ExecutionResult<UpdateItemResponse>>(
            new ExecutionResult<UpdateItemResponse>.Ok(
                new UpdateItemResponse(command.Id, updatedAt)));
    }
}

// ── PATCH  PATCH /demo/catalog/items/patch ─────────────────────────────────────

/// <summary>Partial-update command. Null properties are left unchanged in the store.</summary>
public sealed record PatchItemCommand(
    Guid Id,
    string? Name,
    double? PriceEur,
    int? Quantity);

public sealed record PatchItemResponse(Guid Id, DateTimeOffset UpdatedAt);

[Capability("demo.catalog.items.patch")]
[CapabilityEndpoint("PATCH")]
public sealed class PatchItemHandler : ICapabilityHandler<PatchItemCommand, PatchItemResponse>
{
    private readonly ItemStore _store;

    public PatchItemHandler(ItemStore store) => _store = store;

    public ValueTask<ExecutionResult<PatchItemResponse>> HandleAsync(
        PatchItemCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        var existing = _store.TryGet(command.Id);
        if (existing is null)
            return ValueTask.FromResult<ExecutionResult<PatchItemResponse>>(
                new ExecutionResult<PatchItemResponse>.Fail(
                    new ExecutionError("ITEM_NOT_FOUND", $"No item with id '{command.Id}'.")));

        var updatedAt = DateTimeOffset.UtcNow;
        _store.Save(existing with
        {
            Name = command.Name ?? existing.Name,
            PriceEur = command.PriceEur ?? existing.PriceEur,
            Quantity = command.Quantity ?? existing.Quantity,
        });

        return ValueTask.FromResult<ExecutionResult<PatchItemResponse>>(
            new ExecutionResult<PatchItemResponse>.Ok(
                new PatchItemResponse(command.Id, updatedAt)));
    }
}
