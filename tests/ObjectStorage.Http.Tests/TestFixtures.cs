using Forge.Entity;

namespace Forge.ObjectStorage.Http.Tests;

// ──────────────────────────────────────────────────────────────────────────────────
// Test entity: Random (UuidV4) identity, object-bearing.
// MapObjectOperations() registers all eight routes:
//   - Five metadata routes at api/entities/test-notes
//   - Three content routes at api/objects/test-notes/content
// MapOperations() must skip it (emitting a LogWarning instead) so that
// accidentally registering an OperationEndpointDescriptor does not cause
// duplicate-route conflicts.
// ──────────────────────────────────────────────────────────────────────────────────

[Entity(Path = "test-notes")]
[Identity(IdentityGenerator.Random)]
[ObjectBearing("test-notes-store")]
public partial class TestNote
{
    [Predicate("title")]
    public string Title { get; set; } = "";
}
