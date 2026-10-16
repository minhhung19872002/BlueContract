using BlueContract.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueContract.Api.Controllers;

/// <summary>
/// Signed, short-lived download links (spec §11 "presigned URL ngắn hạn"): issued only after the permission check of the
/// feature that hands them out, so the link itself needs no session — like a presigned S3 URL, but on the portal's host.
/// </summary>
[Route("api/files")]
[AllowAnonymous]
public sealed class FilesController(IFileStorage storage, IUrlSigner signer) : ApiControllerBase
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken ct)
    {
        if (!signer.TryValidate(token, out var objectKey) || await storage.GetContentTypeAsync(objectKey, ct) is not { } contentType)
        {
            return Problem(Error.NotFound("FILE_LINK_EXPIRED"));
        }

        Response.Headers.CacheControl = "private, no-store";
        // Seekable content: the PDF viewer can ask for ranges.
        return File(await storage.DownloadAsync(objectKey, ct), contentType, enableRangeProcessing: true);
    }
}
