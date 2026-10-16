using BlueContract.Api.Auth;
using BlueContract.Application.Common;
using BlueContract.Application.Notifications;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using P = BlueContract.Domain.Identity.Permissions;

namespace BlueContract.Api.Controllers;

/// <summary>Cấu hình luồng ký (spec §9.1-14).</summary>
[Route("api/workflow-templates")]
public sealed class WorkflowTemplatesController(WorkflowService service) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.WorkflowManage, P.TemplateManage)]
    public Task<List<WorkflowTemplateDto>> List([FromQuery] DocumentType? documentType, CancellationToken ct) => service.ListTemplatesAsync(documentType, ct);

    [HttpPost]
    [HasPermission(P.WorkflowManage)]
    public async Task<ActionResult<Guid>> Create(SaveWorkflowTemplateRequest request, CancellationToken ct) =>
        FromResult(await service.SaveTemplateAsync(null, request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.WorkflowManage)]
    public async Task<ActionResult<Guid>> Update(Guid id, SaveWorkflowTemplateRequest request, CancellationToken ct) =>
        FromResult(await service.SaveTemplateAsync(id, request, ct));
}

/// <summary>Workflow on a document: start, approve, return, comment, resubmit, cancel.</summary>
[Route("api/workflows")]
[Authorize]
public sealed class WorkflowsController(WorkflowService service) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<Guid>> Start(StartWorkflowRequest request, CancellationToken ct) => FromResult(await service.StartAsync(request, ct));

    [HttpGet("by-document/{documentId:guid}")]
    public async Task<ActionResult<WorkflowDto?>> ForDocument(Guid documentId, CancellationToken ct) =>
        FromResult(await service.GetForDocumentAsync(documentId, ct));

    [HttpPost("steps/{stepId:guid}/approve")]
    public async Task<ActionResult<WorkflowDto>> Approve(Guid stepId, ApproveStepRequest request, CancellationToken ct) =>
        FromResult(await service.ApproveAsync(stepId, request, ct));

    /// <summary>Trả lại (bắt buộc lý do, chọn bước trả về).</summary>
    [HttpPost("steps/{stepId:guid}/return")]
    public async Task<ActionResult<WorkflowDto>> Return(Guid stepId, ReturnStepRequest request, CancellationToken ct) =>
        FromResult(await service.ReturnAsync(stepId, request, ct));

    /// <summary>Chuyển tiếp ý kiến.</summary>
    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<WorkflowDto>> Comment(Guid id, CommentRequest request, CancellationToken ct) =>
        FromResult(await service.CommentAsync(id, request, ct));

    [HttpPost("{id:guid}/resubmit")]
    public async Task<ActionResult<WorkflowDto>> Resubmit(Guid id, CancellationToken ct) => FromResult(await service.ResubmitAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CommentRequest request, CancellationToken ct) => FromResult(await service.CancelAsync(id, request, ct));
}

/// <summary>Menu Trình ký (spec §6.7) and digital signing (spec §8).</summary>
[Route("api/signing")]
public sealed class SigningController(SigningService signing, WorkflowService workflows) : ApiControllerBase
{
    [HttpGet("providers")]
    [HasPermission(P.SigningInbox)]
    public IReadOnlyList<SigningProviderDto> Providers() => signing.Providers();

    [HttpGet("inbox")]
    [HasPermission(P.SigningInbox)]
    public Task<PagedResult<InboxItemDto>> Inbox([FromQuery] PageQuery query, [FromQuery] InboxFilter filter, CancellationToken ct) =>
        workflows.InboxAsync(query, filter, ct);

    [HttpGet("inbox/count")]
    [HasPermission(P.SigningInbox)]
    public Task<InboxCountDto> InboxCount(CancellationToken ct) => workflows.InboxCountAsync(ct);

    /// <summary>Starts signing a step; server-side providers complete immediately.</summary>
    [HttpPost("steps/{stepId:guid}/sign")]
    [HasPermission(P.SigningSign)]
    public async Task<ActionResult<SigningRequestDto>> Sign(Guid stepId, BeginSignRequest request, CancellationToken ct) =>
        FromResult(await signing.BeginAsync(stepId, request, ct));

    /// <summary>Polls a remote-signing request (user confirming on a phone).</summary>
    [HttpGet("requests/{id:guid}")]
    [HasPermission(P.SigningSign)]
    public async Task<ActionResult<SigningRequestDto>> Status(Guid id, CancellationToken ct) => FromResult(await signing.StatusAsync(id, ct));

    /// <summary>USB token: signature value returned by the CA plugin.</summary>
    [HttpPost("requests/{id:guid}/complete")]
    [HasPermission(P.SigningSign)]
    public async Task<ActionResult<SigningRequestDto>> Complete(Guid id, CompleteSignRequest request, CancellationToken ct) =>
        FromResult(await signing.CompleteAsync(id, request, ct));

    /// <summary>Ký hàng loạt.</summary>
    [HttpPost("batch")]
    [HasPermission(P.SigningSign)]
    public Task<List<BatchSignResultDto>> Batch(BatchSignRequest request, CancellationToken ct) => signing.BatchAsync(request, ct);

    [HttpGet("documents/{documentId:guid}/versions/{versionId:guid}/verify")]
    [Authorize]
    public async Task<ActionResult<VerifyResultDto>> VerifyVersion(Guid documentId, Guid versionId, CancellationToken ct) =>
        FromResult(await signing.VerifyVersionAsync(documentId, versionId, ct));

    /// <summary>Xác minh chữ ký của một tệp PDF bất kỳ.</summary>
    [HttpPost("verify")]
    [Authorize]
    [RequestSizeLimit(FileInspector.MaxUploadBytes + (1024 * 1024))]
    public async Task<ActionResult<VerifyResultDto>> Verify(IFormFile? file, CancellationToken ct)
    {
        var upload = await TemplatesController.ReadUploadAsync(file, ct);
        if (!upload.IsSuccess)
        {
            return Problem(upload.Error!);
        }

        return FromResult(signing.VerifyUpload(upload.Value.Content));
    }
}

[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(NotificationService service) : ApiControllerBase
{
    [HttpGet]
    public Task<NotificationListDto> List([FromQuery] bool unreadOnly, [FromQuery] int take, CancellationToken ct) =>
        service.ListMineAsync(unreadOnly, take == 0 ? 20 : take, ct);

    [HttpPost("read")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await service.MarkReadAsync(null, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await service.MarkReadAsync(id, ct);
        return NoContent();
    }
}
