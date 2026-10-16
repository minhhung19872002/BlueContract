using System.Net;
using System.Net.Http.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Common;
using BlueContract.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Archive;

/// <summary>Spec §11: periodic SHA-256 check of stored files detects a file altered on the object storage.</summary>
[Collection(ApiCollection.Name)]
public class IntegrityTests(ApiFactory factory)
{
    [Fact]
    public async Task Integrity_check_detects_a_tampered_file_and_alerts()
    {
        using var admin = await factory.LoginAsync("admin");
        var healthy = await (await admin.PostAsync(new Uri("/api/admin/integrity/run", UriKind.Relative), null)).Content.ReadFromJsonAsync<IntegrityRunDto>(JsonDefaults.Options);
        healthy!.Run.IsHealthy.ShouldBeTrue(string.Join("; ", healthy.Issues.Select(i => $"{i.Title}: {i.ObjectKey}")));
        healthy.Run.FilesChecked.ShouldBeGreaterThan(10);

        // Alter one stored PDF behind the application's back, then restore it.
        string key;
        byte[] original;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            key = await db.DocumentVersions.Where(v => v.PdfFileKey != null).OrderBy(v => v.CreatedAt).Select(v => v.PdfFileKey!).FirstAsync();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            await using var current = await storage.DownloadAsync(key, CancellationToken.None);
            using var copy = new MemoryStream();
            await current.CopyToAsync(copy);
            original = copy.ToArray();
            using var tampered = new MemoryStream([.. original, .. "%tampered"u8.ToArray()]);
            await storage.UploadAsync(key, tampered, "application/pdf", CancellationToken.None);
        }

        try
        {
            using var response = await admin.PostAsync(new Uri("/api/admin/integrity/run", UriKind.Relative), null);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var run = (await response.Content.ReadFromJsonAsync<IntegrityRunDto>(JsonDefaults.Options))!;
            run.Run.IsHealthy.ShouldBeFalse();
            run.Run.MismatchCount.ShouldBeGreaterThanOrEqualTo(1);
            run.Issues.ShouldContain(i => i.ObjectKey == key && !i.Missing && i.ActualSha256 != i.ExpectedSha256);

            var history = await admin.GetFromJsonAsync<PagedResult<IntegrityRunListItemDto>>("/api/admin/integrity", JsonDefaults.Options);
            history!.Items.First().Id.ShouldBe(run.Run.Id);
            (await admin.GetFromJsonAsync<IntegrityRunDto>($"/api/admin/integrity/{run.Run.Id}", JsonDefaults.Options))!.Issues.ShouldNotBeEmpty();
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            using var restore = new MemoryStream(original);
            await scope.ServiceProvider.GetRequiredService<IFileStorage>().UploadAsync(key, restore, "application/pdf", CancellationToken.None);
        }

        using var buyer = await factory.LoginAsync("muasam");
        (await buyer.PostAsync(new Uri("/api/admin/integrity/run", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A second run is refused while one is still in progress.
        var inProgress = new Domain.Documents.IntegrityCheckRun("Lịch định kỳ", DateTimeOffset.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.IntegrityCheckRuns.Add(inProgress);
            await db.SaveChangesAsync();
        }
        try
        {
            using var refused = await admin.PostAsync(new Uri("/api/admin/integrity/run", UriKind.Relative), null);
            refused.IsSuccessStatusCode.ShouldBeFalse();
            (await refused.Content.ReadAsStringAsync()).ShouldContain("INTEGRITY_ALREADY_RUNNING");
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().IntegrityCheckRuns.Where(r => r.Id == inProgress.Id).ExecuteDeleteAsync();
        }
    }
}
