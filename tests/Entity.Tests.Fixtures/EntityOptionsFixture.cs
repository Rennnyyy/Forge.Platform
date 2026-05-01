using Forge.Entity;

namespace Forge.Entity.Tests.Fixtures;

/// <summary>Sets the global BaseIri once per test run so every entity uses the same prefix.</summary>
public sealed class EntityOptionsFixture
{
    public EntityOptionsFixture()
    {
        EntityOptions.BaseIri = "https://forge-it.net";
    }
}
