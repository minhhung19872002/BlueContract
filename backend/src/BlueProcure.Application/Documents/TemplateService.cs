using BlueProcure.Application.Common;
using BlueProcure.Domain.Documents;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlueProcure.Application.Documents;

public sealed record TemplateListItemDto(Guid Id, string Code, string Name, TemplateKind Kind, string? Description, bool IsBuiltIn, bool IsActive, int ActiveVersionNumber, int VersionCount, DateTimeOffset? LastUploadedAt);

public sealed record TemplateVersionDto(Guid Id, int VersionNumber, string Sha256, long Size, string? Note, DateTimeOffset UploadedAt, string? UploadedByName, bool IsActive);

public sealed record TemplateDetailDto(Guid Id, string Code, string Name, TemplateKind Kind, string? Description, bool IsBuiltIn, bool IsActive, int ActiveVersionNumber, IReadOnlyList<TemplateVersionDto> Versions, IReadOnlyList<string> Placeholders);

public sealed record TemplateFieldsDto(TemplateKind Kind, IReadOnlyList<TemplateField> Fields, IReadOnlyList<TemplateCollection> Collections, IReadOnlyList<string> SignatureAnchors, IReadOnlyList<string> Filters);

public sealed record CreateTemplateRequest(string Code, string Name, TemplateKind Kind, string? Description);

public sealed record UpdateTemplateRequest(string Name, string? Description, bool IsActive);

internal sealed class CreateTemplateRequestValidator : AbstractValidator<CreateTemplateRequest>
{
    public CreateTemplateRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithName("Mã mẫu").Matches("^[A-Za-z0-9_]{2,50}$").WithMessage("Mã mẫu chỉ gồm chữ không dấu, số, gạch dưới (2–50 ký tự).");
        RuleFor(x => x.Name).NotEmpty().WithName("Tên mẫu").MaximumLength(300);
        RuleFor(x => x.Kind).IsInEnum().WithName("Loại mẫu");
    }
}

/// <summary>Quản lý mẫu biểu: upload DOCX templates, validate placeholders, version, preview with sample data (spec §9.1/14).</summary>
public sealed class TemplateService(
    IAppDbContext db,
    IFileStorage storage,
    IDocumentGenerator generator,
    IPdfConverter pdf,
    ICurrentUser currentUser,
    IClock clock)
{
    public Task<List<TemplateListItemDto>> ListAsync(TemplateKind? kind, bool activeOnly, CancellationToken ct)
    {
        var query = db.DocumentTemplates.AsNoTracking();
        if (kind is not null)
        {
            query = query.Where(t => t.Kind == kind);
        }

        if (activeOnly)
        {
            query = query.Where(t => t.IsActive);
        }

        return query.OrderBy(t => t.Kind).ThenBy(t => t.Code)
            .Select(t => new TemplateListItemDto(t.Id, t.Code, t.Name, t.Kind, t.Description, t.IsBuiltIn, t.IsActive, t.ActiveVersionNumber,
                t.Versions.Count, t.Versions.Max(v => (DateTimeOffset?)v.UploadedAt)))
            .ToListAsync(ct);
    }

    public async Task<Result<TemplateDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.AsNoTracking().Include(t => t.Versions).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return Error.NotFound();
        }

        var uploaderIds = template.Versions.Where(v => v.UploadedBy != null).Select(v => v.UploadedBy!.Value).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters().Where(u => uploaderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var versions = template.Versions.OrderByDescending(v => v.VersionNumber)
            .Select(v => new TemplateVersionDto(v.Id, v.VersionNumber, v.Sha256, v.Size, v.Note, v.UploadedAt,
                v.UploadedBy is { } u && names.TryGetValue(u, out var n) ? n : "Hệ thống", v.VersionNumber == template.ActiveVersionNumber))
            .ToList();
        var placeholders = template.ActiveVersion?.Placeholders.Split('\n', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList() ?? [];
        return new TemplateDetailDto(template.Id, template.Code, template.Name, template.Kind, template.Description, template.IsBuiltIn,
            template.IsActive, template.ActiveVersionNumber, versions, placeholders);
    }

    public static TemplateFieldsDto Fields(TemplateKind kind)
    {
        var schema = TemplateFieldCatalog.For(kind);
        return new TemplateFieldsDto(kind, schema.Fields, schema.Collections, schema.SignatureAnchors, [.. TemplateValidator.Filters]);
    }

    public Result<TemplateValidationResult> Validate(TemplateKind kind, UploadedFile file)
    {
        if (FileInspector.Detect(file.Content) != FileKind.Docx)
        {
            return Error.Validation("TEMPLATE_NOT_DOCX");
        }

        IReadOnlyList<string> tags;
        try
        {
            tags = generator.ScanTags(file.Content);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or InvalidOperationException or IOException)
        {
            return Error.Validation("TEMPLATE_NOT_DOCX");
        }

        return TemplateValidator.Validate(kind, tags);
    }

    public async Task<Result<Guid>> CreateAsync(CreateTemplateRequest r, UploadedFile file, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.DocumentTemplates.IgnoreQueryFilters().AnyAsync(t => t.Code == code, ct))
        {
            return Error.Conflict("TEMPLATE_CODE_EXISTS");
        }

        var validation = Validate(r.Kind, file);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        if (!validation.Value.IsValid)
        {
            return Error.Validation("TEMPLATE_PLACEHOLDERS_INVALID");
        }

        var template = new DocumentTemplate(code, r.Name, r.Kind, r.Description, isBuiltIn: false);
        db.DocumentTemplates.Add(template);
        await StoreVersionAsync(template, file.Content, validation.Value.Tags, "Tải lên lần đầu", ct);
        await db.SaveChangesAsync(ct);
        return template.Id;
    }

    public async Task<Result<int>> UploadVersionAsync(Guid id, UploadedFile file, string? note, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.Include(t => t.Versions).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return Error.NotFound();
        }

        var validation = Validate(template.Kind, file);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        if (!validation.Value.IsValid)
        {
            return Error.Validation("TEMPLATE_PLACEHOLDERS_INVALID");
        }

        var version = await StoreVersionAsync(template, file.Content, validation.Value.Tags, note, ct);
        await db.SaveChangesAsync(ct);
        return version.VersionNumber;
    }

    public async Task<Result> UpdateAsync(Guid id, UpdateTemplateRequest r, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return Result.Failure(Error.NotFound());
        }

        template.Update(r.Name, r.Description, r.IsActive);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ActivateVersionAsync(Guid id, int versionNumber, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.Include(t => t.Versions).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return Result.Failure(Error.NotFound());
        }

        template.Activate(versionNumber);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Raw DOCX of a version (active one by default).</summary>
    public async Task<Result<(byte[] Content, string FileName)>> DownloadAsync(Guid id, int? versionNumber, CancellationToken ct)
    {
        var loaded = await LoadVersionAsync(id, versionNumber, ct);
        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var (template, version) = loaded.Value;
        return (await ReadAllAsync(version.FileKey, ct), $"{template.Code}_v{version.VersionNumber}.docx");
    }

    /// <summary>Renders the template with sample data ("xem trước với dữ liệu mẫu").</summary>
    public async Task<Result<(byte[] Content, string FileName, string ContentType)>> PreviewAsync(Guid id, int? versionNumber, bool asPdf, CancellationToken ct)
    {
        var loaded = await LoadVersionAsync(id, versionNumber, ct);
        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var (template, version) = loaded.Value;
        var docx = generator.Render(await ReadAllAsync(version.FileKey, ct), TemplateFieldCatalog.SampleData(template.Kind));
        var name = $"{template.Code}_v{version.VersionNumber}_xem-truoc";
        return asPdf
            ? (await pdf.ConvertToPdfAsync(docx, name, ct), name + ".pdf", "application/pdf")
            : (docx, name + ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }

    /// <summary>Renders the active version of <paramref name="templateId"/> with real data (used by business modules).</summary>
    public async Task<Result<byte[]>> RenderAsync(Guid templateId, System.Text.Json.Nodes.JsonObject data, CancellationToken ct)
    {
        var loaded = await LoadVersionAsync(templateId, null, ct);
        return loaded.IsSuccess ? generator.Render(await ReadAllAsync(loaded.Value.Version.FileKey, ct), data) : loaded.Error!;
    }

    private async Task<DocumentTemplateVersion> StoreVersionAsync(DocumentTemplate template, byte[] content, IReadOnlyList<string> tags, string? note, CancellationToken ct)
    {
        var number = template.Versions.Count + 1;
        using var stream = new MemoryStream(content);
        var stored = await storage.UploadAsync($"templates/{template.Id:N}/v{number}.docx", stream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ct);
        return template.AddVersion(stored.ObjectKey, stored.Sha256, stored.Size, tags.Distinct().ToList(), note, currentUser.UserId, clock.UtcNow);
    }

    private async Task<Result<(DocumentTemplate Template, DocumentTemplateVersion Version)>> LoadVersionAsync(Guid id, int? versionNumber, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.AsNoTracking().Include(t => t.Versions).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return Error.NotFound();
        }

        var number = versionNumber ?? template.ActiveVersionNumber;
        var version = template.Versions.FirstOrDefault(v => v.VersionNumber == number);
        return version is null ? Error.NotFound("TEMPLATE_VERSION_NOT_FOUND") : (template, version);
    }

    private async Task<byte[]> ReadAllAsync(string key, CancellationToken ct)
    {
        await using var stream = await storage.DownloadAsync(key, ct);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, ct);
        return copy.ToArray();
    }
}
