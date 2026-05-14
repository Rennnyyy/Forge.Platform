using Forge.Branch.Merge;
using Forge.Capability;
using Forge.Execution;

namespace Forge.Branch.Http;

/// <summary>Inbound command for the <c>branch.merge</c> capability.</summary>
/// <param name="SourceBranchIri">Named graph to read entities from (read-only).</param>
/// <param name="TargetBranchIri">Named graph to upsert entities into.</param>
public sealed record MergeBranchesCommand(string SourceBranchIri, string TargetBranchIri);

/// <summary>Response returned by the <c>branch.merge</c> capability.</summary>
/// <param name="SourceBranchIri">IRI of the source named graph.</param>
/// <param name="TargetBranchIri">IRI of the target named graph.</param>
/// <param name="CreatedCount">Number of entities created in the target graph.</param>
/// <param name="UpdatedCount">Number of entities updated in the target graph.</param>
/// <param name="TotalCount">Total entities written (<c>Created + Updated</c>).</param>
/// <param name="IsEmpty">True when the merge was a no-op (source had nothing new).</param>
public sealed record MergeBranchesResponse(
    string SourceBranchIri,
    string TargetBranchIri,
    int CreatedCount,
    int UpdatedCount,
    int TotalCount,
    bool IsEmpty);

/// <summary>
/// Capability handler that merges all entities from a source named graph into a target
/// named graph as a single atomic transaction. Registered under the identity
/// <c>branch.merge</c>; the HTTP transport maps to <c>POST api/capabilities/branch/merge</c>.
/// See Branch ADR-0007.
/// </summary>
[Capability("branch.merge")]
internal sealed class MergeBranchesHandler : ICapabilityHandler<MergeBranchesCommand, MergeBranchesResponse>
{
    private readonly BranchMergeService _mergeService;

    public MergeBranchesHandler(BranchMergeService mergeService)
    {
        ArgumentNullException.ThrowIfNull(mergeService);
        _mergeService = mergeService;
    }

    /// <inheritdoc/>
    public async ValueTask<ExecutionResult<MergeBranchesResponse>> HandleAsync(
        MergeBranchesCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.SourceBranchIri))
            return new ExecutionResult<MergeBranchesResponse>.Fail(
                new ExecutionError("INVALID_SOURCE",
                    "SourceBranchIri must not be null or whitespace."));

        if (string.IsNullOrWhiteSpace(command.TargetBranchIri))
            return new ExecutionResult<MergeBranchesResponse>.Fail(
                new ExecutionError("INVALID_TARGET",
                    "TargetBranchIri must not be null or whitespace."));

        if (!Uri.TryCreate(command.SourceBranchIri, UriKind.Absolute, out _))
            return new ExecutionResult<MergeBranchesResponse>.Fail(
                new ExecutionError("INVALID_SOURCE_IRI",
                    $"'{command.SourceBranchIri}' is not a valid absolute IRI."));

        if (!Uri.TryCreate(command.TargetBranchIri, UriKind.Absolute, out _))
            return new ExecutionResult<MergeBranchesResponse>.Fail(
                new ExecutionError("INVALID_TARGET_IRI",
                    $"'{command.TargetBranchIri}' is not a valid absolute IRI."));

        BranchMergeResult result;
        try
        {
            result = await _mergeService
                .MergeAsync(command.SourceBranchIri, command.TargetBranchIri, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return new ExecutionResult<MergeBranchesResponse>.Fail(
                new ExecutionError("INVALID_MERGE", ex.Message));
        }
        catch (MergePlanUnresolvableTypeException ex)
        {
            return new ExecutionResult<MergeBranchesResponse>.Fail(
                new ExecutionError("UNRESOLVABLE_TYPE", ex.Message));
        }
        catch (MergePlanHydrationException ex)
        {
            return new ExecutionResult<MergeBranchesResponse>.Fail(
                new ExecutionError("HYDRATION_FAILED", ex.Message));
        }
        catch (MergePlanCycleException ex)
        {
            return new ExecutionResult<MergeBranchesResponse>.Fail(
                new ExecutionError("MERGE_CYCLE", ex.Message));
        }

        return new ExecutionResult<MergeBranchesResponse>.Ok(new MergeBranchesResponse(
            result.SourceBranchIri,
            result.TargetBranchIri,
            result.CreatedCount,
            result.UpdatedCount,
            result.TotalCount,
            result.IsEmpty));
    }
}
