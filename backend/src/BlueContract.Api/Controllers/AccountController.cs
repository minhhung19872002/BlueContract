using BlueContract.Application.Common;
using BlueContract.Application.Signing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueContract.Api.Controllers;

/// <summary>The signed-in user's own settings (internal staff and supplier users alike).</summary>
[Route("api/account")]
[Authorize]
public sealed class AccountController(SignatureImageService images, CertificateRegistrationService certificates) : ApiControllerBase
{
    /// <summary>Signing certificate registered by the user (spec §9.2-7 "đăng ký chứng thư số").</summary>
    [HttpGet("certificate")]
    public Task<RegisteredCertificateDto> GetCertificate(CancellationToken ct) => certificates.GetMineAsync(ct);

    /// <summary>Registers the public certificate (.cer / .crt / .pem) of the token or remote-signing account.</summary>
    [HttpPost("certificate")]
    [RequestSizeLimit(FileInspector.MaxUploadBytes + (1024 * 1024))]
    public async Task<ActionResult<RegisteredCertificateDto>> RegisterCertificate(IFormFile? file, CancellationToken ct)
    {
        var upload = await TemplatesController.ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(await certificates.RegisterMineAsync(upload.Value, ct)) : Problem(upload.Error!);
    }

    [HttpDelete("certificate")]
    public async Task<IActionResult> RemoveCertificate(CancellationToken ct) => FromResult(await certificates.RemoveMineAsync(ct));

    /// <summary>Handwritten signature ("signature") or initials ("initial") drawn in the visible signature (spec §8).</summary>
    [HttpPost("signature-images/{kind}")]
    [RequestSizeLimit(FileInspector.MaxUploadBytes + (1024 * 1024))]
    public async Task<IActionResult> SetImage(string kind, IFormFile? file, CancellationToken ct)
    {
        if (Parse(kind) is not { } k)
        {
            return NotFound();
        }

        var upload = await TemplatesController.ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(await images.SetMineAsync(k, upload.Value, ct)) : Problem(upload.Error!);
    }

    [HttpGet("signature-images/{kind}")]
    public async Task<IActionResult> GetImage(string kind, CancellationToken ct)
    {
        if (Parse(kind) is not { } k)
        {
            return NotFound();
        }

        var image = await images.GetMineAsync(k, ct);
        if (!image.IsSuccess)
        {
            return Problem(image.Error!);
        }

        Response.Headers.CacheControl = "private, no-store";
        return File(image.Value.Content, image.Value.ContentType);
    }

    [HttpDelete("signature-images/{kind}")]
    public async Task<IActionResult> RemoveImage(string kind, CancellationToken ct) =>
        Parse(kind) is { } k ? FromResult(await images.RemoveMineAsync(k, ct)) : NotFound();

    private static SignatureImageKind? Parse(string kind) => kind switch
    {
        "signature" => SignatureImageKind.Signature,
        "initial" => SignatureImageKind.Initial,
        _ => null,
    };
}
