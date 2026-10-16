using System.Text.Json.Nodes;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Payments;

/// <summary>Renders the payment minutes from their templates and sends them into their signing workflows.</summary>
public sealed class PaymentDocuments(IAppDbContext db, TemplateService templates, DocumentService documents, WorkflowService workflows, ICurrentUser current)
{
    /// <summary>Creates the document, or adds a new version (draft / returned minutes only — callers check).</summary>
    public async Task<Result<Guid>> RenderAsync(Guid? documentId, TemplateKind kind, DocumentType type, string title, string ownerType, Guid ownerId,
        Guid supplierId, Guid contractId, JsonObject data, string reason, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.AsNoTracking().Where(t => t.Kind == kind && t.IsActive).OrderBy(t => t.Code).FirstOrDefaultAsync(ct);
        if (template is null)
        {
            return Error.Validation("PAYMENT_TEMPLATE_MISSING");
        }

        var rendered = await templates.RenderAsync(template.Id, data, ct);
        if (!rendered.IsSuccess)
        {
            return rendered.Error!;
        }

        if (documentId is { } id)
        {
            var existing = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == id, ct);
            await documents.AddDocxVersionAsync(existing, rendered.Value, reason, current.UserId, current.FullName, ct);
            return existing.Id;
        }

        var document = await documents.CreateFromDocxAsync(type, title, ownerType, ownerId, supplierId, contractId, template.Id, rendered.Value, reason, ct);
        return document.Id;
    }

    /// <summary>Gửi ký (lần đầu) hoặc trình ký lại sau khi bị trả lại. Saved by the caller.</summary>
    public async Task<Result> SubmitAsync(Guid documentId, CancellationToken ct)
    {
        var returned = await db.WorkflowInstances.Include("_steps").Include("_events")
            .FirstOrDefaultAsync(w => w.DocumentId == documentId && w.Status == WorkflowStatus.Returned, ct);
        if (returned is not null)
        {
            var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == documentId, ct);
            await workflows.ResubmitForModuleAsync(returned, document, current.FullName ?? string.Empty, ct);
            return Result.Success();
        }

        var started = await workflows.StartAsync(new StartWorkflowRequest(documentId, null, null, null, null), ct);
        return started.IsSuccess ? Result.Success() : Result.Failure(started.Error!);
    }

    public async Task CancelAsync(Guid? documentId, string reason, CancellationToken ct)
    {
        if (documentId is not { } id)
        {
            return;
        }

        var wf = await db.WorkflowInstances.Include("_steps").Include("_events")
            .FirstOrDefaultAsync(w => w.DocumentId == id && (w.Status == WorkflowStatus.Returned || w.Status == WorkflowStatus.Running), ct);
        wf?.Cancel(current.UserId ?? Guid.Empty, current.FullName ?? string.Empty, reason, DateTimeOffset.UtcNow);
        (await db.Documents.FirstAsync(d => d.Id == id, ct)).Cancel();
    }
}
