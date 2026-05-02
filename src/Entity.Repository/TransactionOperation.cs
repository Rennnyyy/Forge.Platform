namespace Forge.Entity.Repository;

/// <summary>
/// Abstract base for a single operation inside an <see cref="EntityTransaction"/>.
/// </summary>
public abstract class TransactionOperation
{
    private static readonly IOperationAspect _noOp = global::Forge.Entity.Repository.Aspect.NoOp;

    /// <summary>The IRI of the entity targeted by this operation.</summary>
    public abstract string EntityIri { get; }

    /// <summary>
    /// The validation policy that applies to this operation.
    /// Defaults to <see cref="Aspect.NoOp"/> — no validation.
    /// See Aspects ADR-0003.
    /// </summary>
    public IOperationAspect Aspect { get; init; } = _noOp;
}

/// <summary>
/// Abstract base for operations that write an entity (Create or Update). Exposes the
/// entity in a non-generic way so backends can project triples via
/// <see cref="IRdfMapper.ProjectEntity"/> without knowing <typeparamref name="T"/> at the
/// transaction executor call site.
/// </summary>
public abstract class EntityWriteOperation : TransactionOperation
{
    /// <summary>The entity that will be persisted.</summary>
    public abstract IEntity Entity { get; }

    /// <summary>The write mode to use when persisting the entity.</summary>
    public abstract WriteMode Mode { get; }
}

/// <summary>
/// Adds a new entity to the store. Fails (and rolls back the transaction) if an entity
/// with the same IRI already exists.
/// </summary>
public sealed class CreateOperation<T> : EntityWriteOperation where T : class, IEntity
{
    public CreateOperation(T entity) => TypedEntity = entity;

    /// <summary>The strongly-typed entity to create.</summary>
    public T TypedEntity { get; }

    /// <inheritdoc/>
    public override IEntity Entity => TypedEntity;

    /// <inheritdoc/>
    public override WriteMode Mode => WriteMode.Create;

    /// <inheritdoc/>
    public override string EntityIri => TypedEntity.Iri;
}

/// <summary>
/// Replaces an existing entity in the store (full PUT semantics). Deletes all current
/// triples for the entity's IRI and writes the projected triples.
/// </summary>
public sealed class UpdateOperation<T> : EntityWriteOperation where T : class, IEntity
{
    public UpdateOperation(T entity) => TypedEntity = entity;

    /// <summary>The strongly-typed entity to update.</summary>
    public T TypedEntity { get; }

    /// <inheritdoc/>
    public override IEntity Entity => TypedEntity;

    /// <inheritdoc/>
    public override WriteMode Mode => WriteMode.Replace;

    /// <inheritdoc/>
    public override string EntityIri => TypedEntity.Iri;
}

/// <summary>
/// Deletes every triple whose subject is <see cref="Iri"/> from the store.
/// </summary>
public sealed class DeleteOperation : TransactionOperation
{
    public DeleteOperation(string iri) => Iri = iri;

    /// <summary>The IRI of the entity to delete.</summary>
    public string Iri { get; }

    /// <summary>
    /// The CLR type of the entity being deleted. Set when a typed aspect is declared via
    /// <c>EntityTransaction.Delete&lt;T&gt;(iri, aspect)</c>; <c>null</c> for NoOp deletes.
    /// </summary>
    public Type? EntityType { get; init; }

    /// <inheritdoc/>
    public override string EntityIri => Iri;
}
