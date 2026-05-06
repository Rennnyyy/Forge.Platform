using System.Collections.Concurrent;

namespace Forge.Application.Sample;

/// <summary>
/// Thread-safe in-memory catalog store used by the demo sample handlers.
/// Pre-seeded with one item so GET requests work before CREATE is called.
/// </summary>
public sealed class ItemStore
{
    private readonly ConcurrentDictionary<Guid, StoredItem> _items = new();

    public ItemStore()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _items[id] = new StoredItem(
            id,
            Name: "Seed Widget",
            IsAvailable: true,
            Quantity: 10,
            PriceEur: 9.99,
            Tags: ["seed", "demo"],
            Dimensions: new ItemDimensions(5.0, 5.0, 5.0),
            CreatedAt: DateTimeOffset.UtcNow);
    }

    public StoredItem? TryGet(Guid id) =>
        _items.TryGetValue(id, out var item) ? item : null;

    public void Save(StoredItem item) =>
        _items[item.Id] = item;
}

public sealed record StoredItem(
    Guid Id,
    string Name,
    bool IsAvailable,
    int Quantity,
    double PriceEur,
    IReadOnlyList<string> Tags,
    ItemDimensions Dimensions,
    DateTimeOffset CreatedAt);
