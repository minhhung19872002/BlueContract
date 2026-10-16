using BlueContract.Domain.Documents;
using BlueContract.Domain.Workflows;

namespace BlueContract.Application.Workflows;

public enum WorkflowChangeKind
{
    Started = 0,
    Advanced = 1,
    Completed = 2,
    Returned = 3,
    Resubmitted = 4,
    Cancelled = 5,
    Restarted = 6,
}

/// <summary>What happened to a workflow; <see cref="CompletedStep"/> is the step just acted on (if any).</summary>
public sealed record WorkflowChange(
    WorkflowInstance Workflow,
    Document Document,
    WorkflowChangeKind Kind,
    WorkflowStepInstance? CompletedStep,
    string? Note);

/// <summary>
/// Lets business modules react to their documents' workflows (contract status, appendix application...). Called inside
/// the same unit of work, before SaveChanges, so the module's changes commit atomically with the workflow.
/// </summary>
public interface IWorkflowObserver
{
    bool Handles(DocumentType type);

    Task OnChangedAsync(WorkflowChange change, CancellationToken ct);
}
