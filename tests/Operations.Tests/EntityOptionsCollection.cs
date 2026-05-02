using Forge.Entity;
using Forge.Entity.Tests.Fixtures;

namespace Forge.Operations.Tests;

/// <summary>Wires <see cref="EntityOptionsFixture"/> for the EntityOptions collection in this assembly.</summary>
[CollectionDefinition("EntityOptions")]
public sealed class EntityOptionsCollection : ICollectionFixture<EntityOptionsFixture> { }
