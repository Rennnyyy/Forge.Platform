using Forge.Entity.Tests.Fixtures;

namespace Forge.Entity.Aspects.Tests;

/// <summary>Apply the EntityOptionsFixture to every test class in this assembly.</summary>
[CollectionDefinition("EntityOptions")]
public sealed class EntityOptionsCollection : ICollectionFixture<EntityOptionsFixture> { }
