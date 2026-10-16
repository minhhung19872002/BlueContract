using BlueProcure.Application.Common;
using BlueProcure.Application.Documents;
using BlueProcure.Domain.Documents;
using BlueProcure.Infrastructure.Documents.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlueProcure.Infrastructure.Persistence.Seeding;

/// <summary>Installs the built-in templates (spec §10) on first start. Existing templates are never overwritten.</summary>
public sealed class TemplateSeeder(AppDbContext db, IFileStorage storage, IDocumentGenerator generator, IClock clock, ILogger<TemplateSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.DocumentTemplates.IgnoreQueryFilters().Select(t => t.Code).ToListAsync(ct);
        var created = 0;
        foreach (var builtIn in BuiltInTemplates.All.Where(t => !existing.Contains(t.Code)))
        {
            var bytes = builtIn.Build();
            var validation = TemplateValidator.Validate(builtIn.Kind, generator.ScanTags(bytes));
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(
                    $"Built-in template {builtIn.Code} has invalid placeholders: {string.Join("; ", validation.Errors.Select(e => e.Tag + " → " + e.Message))}");
            }

            var template = new DocumentTemplate(builtIn.Code, builtIn.Name, builtIn.Kind, builtIn.Description, isBuiltIn: true);
            using var stream = new MemoryStream(bytes);
            var stored = await storage.UploadAsync($"templates/{template.Id:N}/v1.docx", stream, DocumentService.DocxContentType, ct);
            template.AddVersion(stored.ObjectKey, stored.Sha256, stored.Size, validation.Tags.Distinct().ToList(), "Mẫu chuẩn của hệ thống", null, clock.UtcNow);
            db.DocumentTemplates.Add(template);
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Installed {Count} built-in templates", created);
        }
    }

    /// <summary>Writes all built-in templates to <paramref name="directory"/> (`--export-templates`).</summary>
    public static IReadOnlyList<string> Export(string directory)
    {
        Directory.CreateDirectory(directory);
        var written = new List<string>();
        foreach (var t in BuiltInTemplates.All)
        {
            var path = Path.Combine(directory, t.FileName);
            File.WriteAllBytes(path, t.Build());
            written.Add(path);
        }

        return written;
    }
}
