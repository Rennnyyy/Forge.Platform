using Forge.Entity;
using Shouldly;

namespace Forge.Branch.Tests;

/// <summary>
/// Tests for the <see cref="Snapshot"/> entity type and its generator-derived IRI.
/// See Branch ADR-0002.
/// </summary>
public sealed class SnapshotEntityTests
{
    // ════════════════════════════════════════════════════════════════════════
    // 1. IRI shape — inherits /branches/ path from Branch
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Snapshot_Iri_is_computed_from_Name_under_branches_path()
    {
        var snapshot = new Snapshot { Name = "v1.0.0", SnapshotAt = DateTimeOffset.UtcNow };

        snapshot.Iri.ShouldBe($"{EntityOptions.Current.BaseIri.TrimEnd('/')}/branches/v1.0.0");
    }

    [Fact]
    public void Snapshot_Iri_is_sealed_after_first_access()
    {
        var snapshot = new Snapshot { Name = "v2.0.0", SnapshotAt = DateTimeOffset.UtcNow };
        _ = snapshot.Iri;

        snapshot.IsIdentitySealed.ShouldBeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Inherited Branch properties
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Snapshot_inherits_Name_Description_CreatedAt_from_Branch()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new Snapshot
        {
            Name = "release-1",
            Description = "First release",
            CreatedAt = now,
            SnapshotAt = now,
        };

        snapshot.Name.ShouldBe("release-1");
        snapshot.Description.ShouldBe("First release");
        snapshot.CreatedAt.ShouldBe(now);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Snapshot-own properties default to null/zero
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Snapshot_SemVer_properties_default_to_null()
    {
        var snapshot = new Snapshot { Name = "v1.0.0", SnapshotAt = DateTimeOffset.UtcNow };

        snapshot.SemVerMajor.ShouldBeNull();
        snapshot.SemVerMinor.ShouldBeNull();
        snapshot.SemVerPatch.ShouldBeNull();
        snapshot.SemVerPreRelease.ShouldBeNull();
    }

    [Fact]
    public void Snapshot_SemVer_properties_can_be_set()
    {
        var snapshot = new Snapshot
        {
            Name = "v1.2.3-alpha.1",
            SnapshotAt = DateTimeOffset.UtcNow,
            SemVerMajor = 1,
            SemVerMinor = 2,
            SemVerPatch = 3,
            SemVerPreRelease = "alpha.1",
        };

        snapshot.SemVerMajor.ShouldBe(1);
        snapshot.SemVerMinor.ShouldBe(2);
        snapshot.SemVerPatch.ShouldBe(3);
        snapshot.SemVerPreRelease.ShouldBe("alpha.1");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. Snapshot IS-A Branch at runtime (Liskov; guarded by SnapshotGuardedTransactionalStore)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Snapshot_is_assignable_to_Branch()
    {
        var snapshot = new Snapshot { Name = "v1.0.0", SnapshotAt = DateTimeOffset.UtcNow };

        (snapshot is Branch).ShouldBeTrue();
    }

    [Fact]
    public void Snapshot_implements_IEntity()
    {
        var snapshot = new Snapshot { Name = "v1.0.0", SnapshotAt = DateTimeOffset.UtcNow };

        (snapshot is IEntity).ShouldBeTrue();
    }
}
