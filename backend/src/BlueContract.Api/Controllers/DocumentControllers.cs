using BlueContract.Api.Auth;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Domain.Documents;
using BlueContract.Infrastructure.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using P = BlueContract.Domain.Identity.Permissions;

namespace BlueContract.Api.Controllers;

/// <summary>Quản lý mẫu biểu (spec §9.1/14).</summary>
[Route("api/templates")]
public sealed class TemplatesController(TemplateService service) : ApiControllerBase
{
    private const long UploadLimit = FileInspector.MaxUploadBytes + (1024 * 1024);

    [HttpGet]
    [HasPermission(P.TemplateManage, P.ContractCreate, P.PaymentManage)]
    public Task<List<TemplateListItemDto>> List([FromQuery] TemplateKind? kind, [FromQuery] bool activeOnly, CancellationToken ct) =>
        service.ListAsync(kind, activeOnly, ct);

    [HttpGet("fields")]
    [HasPermission(P.TemplateManage)]
    public TemplateFieldsDto Fields([FromQuery] TemplateKind kind) => TemplateService.Fields(kind);

    [HttpGet("{id:guid}")]
    [HasPermission(P.TemplateManage)]
    public async Task<ActionResult<TemplateDetailDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    /// <summary>Checks placeholders of a DOCX without saving it.</summary>
    [HttpPost("validate")]
    [HasPermission(P.TemplateManage)]
    [RequestSizeLimit(UploadLimit)]
    public async Task<ActionResult<TemplateValidationResult>> Validate([FromForm] TemplateKind kind, IFormFile? file, CancellationToken ct)
    {
        var upload = await ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(service.Validate(kind, upload.Value)) : Problem(upload.Error!);
    }

    [HttpPost]
    [HasPermission(P.TemplateManage)]
    [RequestSizeLimit(UploadLimit)]
    public async Task<ActionResult<Guid>> Create([FromForm] CreateTemplateRequest request, IFormFile? file, CancellationToken ct)
    {
        var upload = await ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(await service.CreateAsync(request, upload.Value, ct)) : Problem(upload.Error!);
    }

    [HttpPost("{id:guid}/versions")]
    [HasPermission(P.TemplateManage)]
    [RequestSizeLimit(UploadLimit)]
    public async Task<ActionResult<int>> UploadVersion(Guid id, IFormFile? file, [FromForm] string? note, CancellationToken ct)
    {
        var upload = await ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(await service.UploadVersionAsync(id, upload.Value, note, ct)) : Problem(upload.Error!);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(P.TemplateManage)]
    public async Task<IActionResult> Update(Guid id, UpdateTemplateRequest request, CancellationToken ct) => FromResult(await service.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/versions/{version:int}/activate")]
    [HasPermission(P.TemplateManage)]
    public async Task<IActionResult> Activate(Guid id, int version, CancellationToken ct) => FromResult(await service.ActivateVersionAsync(id, version, ct));

    [HttpGet("{id:guid}/download")]
    [HasPermission(P.TemplateManage)]
    public async Task<IActionResult> Download(Guid id, [FromQuery] int? version, CancellationToken ct)
    {
        var result = await service.DownloadAsync(id, version, ct);
        return result.IsSuccess
            ? File(result.Value.Content, DocumentService.DocxContentType, result.Value.FileName)
            : Problem(result.Error!);
    }

    /// <summary>Renders the template with sample data ("xem trước với dữ liệu mẫu").</summary>
    [HttpGet("{id:guid}/preview")]
    [HasPermission(P.TemplateManage)]
    public async Task<IActionResult> Preview(Guid id, [FromQuery] int? version, [FromQuery] string format, CancellationToken ct)
    {
        var result = await service.PreviewAsync(id, version, !string.Equals(format, "docx", StringComparison.OrdinalIgnoreCase), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error!);
    }

    internal static async Task<Result<UploadedFile>> ReadUploadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Error.Validation("FILE_REQUIRED");
        }

        if (file.Length > FileInspector.MaxUploadBytes)
        {
            return Error.Validation("FILE_TOO_LARGE");
        }

        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);
        return new UploadedFile(Path.GetFileName(file.FileName), file.ContentType, memory.ToArray());
    }
}

/// <summary>Documents, versions, PDF rendition and online editing.</summary>
[Route("api/documents")]
[Authorize]
public sealed class DocumentsController(DocumentService service, IOptions<OnlyOfficeOptions> onlyOffice) : ApiControllerBase
{
    [HttpPost("samples")]
    [HasPermission(P.TemplateManage)]
    public async Task<ActionResult<Guid>> CreateSample(CreateSampleDocumentRequest request, CancellationToken ct) =>
        FromResult(await service.CreateSampleAsync(request, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    [HttpPost("{id:guid}/versions")]
    [RequestSizeLimit(FileInspector.MaxUploadBytes + (1024 * 1024))]
    public async Task<ActionResult<Guid>> UploadVersion(Guid id, IFormFile? file, [FromForm] string? reason, CancellationToken ct)
    {
        var upload = await TemplatesController.ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(await service.UploadVersionAsync(id, upload.Value, reason, ct)) : Problem(upload.Error!);
    }

    [HttpPost("{id:guid}/versions/{versionId:guid}/pdf")]
    public async Task<ActionResult<DocumentVersionDto>> RenderPdf(Guid id, Guid versionId, CancellationToken ct) =>
        FromResult(await service.RenderPdfAsync(id, versionId, ct));

    /// <summary>Short-lived presigned link (format = pdf | docx).</summary>
    [HttpGet("{id:guid}/versions/{versionId:guid}/link")]
    public async Task<ActionResult<FileLinkDto>> Link(Guid id, Guid versionId, [FromQuery] string format, CancellationToken ct) =>
        FromResult(await service.GetFileLinkAsync(id, versionId, format ?? "pdf", ct));

    /// <summary>Streams the file through the API (PDF viewer).</summary>
    [HttpGet("{id:guid}/versions/{versionId:guid}/file")]
    public async Task<IActionResult> GetFile(Guid id, Guid versionId, [FromQuery] string format, CancellationToken ct)
    {
        var result = await service.ReadFileAsync(id, versionId, format ?? "pdf", ct);
        if (!result.IsSuccess)
        {
            return Problem(result.Error!);
        }

        Response.Headers.CacheControl = "private, no-store";
        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("{id:guid}/edit-sessions")]
    public async Task<ActionResult<EditorSessionDto>> StartEdit(Guid id, StartEditRequest request, CancellationToken ct) =>
        FromResult(await service.StartEditSessionAsync(id, request, onlyOffice.Value.ApiBaseUrlForEditor, ct));
}

/// <summary>Endpoints called server-to-server by ONLYOFFICE Docs (no user session; signed URL tokens + JWT).</summary>
[Route("api/onlyoffice")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class OnlyOfficeController(DocumentService service) : ApiControllerBase
{
    [HttpGet("files/{key}")]
    public async Task<IActionResult> GetFile(string key, [FromQuery] string? token, CancellationToken ct)
    {
        var result = await service.GetEditorFileAsync(key, token, ct);
        return result.IsSuccess ? File(result.Value, DocumentService.DocxContentType, "document.docx") : Problem(result.Error!);
    }

    /// <summary>Save callback. ONLYOFFICE expects {"error":0} on success.</summary>
    [HttpPost("callback/{key}")]
    public async Task<IActionResult> Callback(string key, [FromQuery] string? token, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var result = await service.HandleEditorCallbackAsync(key, token, body, Request.Headers.Authorization.ToString(), ct);
        return Ok(new { error = result.IsSuccess ? 0 : 1 });
    }
}
