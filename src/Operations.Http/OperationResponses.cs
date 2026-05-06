namespace Forge.Operations.Http;

/// <summary>
/// Response returned when an entity is successfully created.
/// </summary>
/// <param name="Iri">The IRI of the newly created entity.</param>
public sealed record OperationCreatedResponse(string Iri);

/// <summary>
/// Response returned when an entity is successfully updated.
/// </summary>
/// <param name="Iri">The IRI of the updated entity.</param>
public sealed record OperationUpdatedResponse(string Iri);

/// <summary>
/// Response returned when an entity is successfully deleted.
/// </summary>
public sealed record OperationDeletedResponse();

/// <summary>
/// Response returned when a list of entities is queried.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
/// <param name="Items">The list of entities.</param>
public sealed record OperationListResponse<T>(IReadOnlyList<T> Items) where T : class;
