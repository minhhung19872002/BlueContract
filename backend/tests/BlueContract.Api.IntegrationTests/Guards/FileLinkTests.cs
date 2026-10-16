using System.Net;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Common;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Guards;

/// <summary>
/// AUDIT-1 §13.1 / spec §11 "link tải file là presigned URL ngắn hạn": download links go through the portal's own
/// address (/api/files/…, behind the same nginx), never to the storage host ("localhost:47900" only works on the
/// server itself). They are signed, short-lived and need no session.
/// </summary>
[Collection(ApiCollection.Name)]
public class FileLinkTests(ApiFactory factory)
{
    [Fact]
    public async Task Download_links_go_through_the_portal_and_expire()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var bytes = "Ủy nhiệm chi"u8.ToArray();
        using (var input = new MemoryStream(bytes))
        {
            await storage.UploadAsync("tests/link.txt", input, "text/plain", CancellationToken.None);
        }

        var link = await storage.GetPresignedDownloadUrlAsync("tests/link.txt", TimeSpan.FromMinutes(5), CancellationToken.None);
        link.IsAbsoluteUri.ShouldBeFalse("links are relative to the portal, whatever host the user reached it by");
        link.OriginalString.ShouldStartWith("/api/files/");

        using var anonymous = factory.CreateClient();
        using (var served = await anonymous.GetAsync(link))
        {
            served.StatusCode.ShouldBe(HttpStatusCode.OK);
            served.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
            (await served.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);
            served.Headers.CacheControl!.NoStore.ShouldBeTrue();
        }

        // Tampered or expired links are refused.
        using (var tampered = await anonymous.GetAsync(new Uri(link.OriginalString[..^4] + "AAAA", UriKind.Relative)))
        {
            tampered.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var expired = await storage.GetPresignedDownloadUrlAsync("tests/link.txt", TimeSpan.FromMilliseconds(1), CancellationToken.None);
        await Task.Delay(50);
        using (var late = await anonymous.GetAsync(expired))
        {
            late.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }
}
