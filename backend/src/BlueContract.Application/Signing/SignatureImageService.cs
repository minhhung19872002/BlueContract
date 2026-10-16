using BlueContract.Application.Common;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Signing;

/// <summary>Which image of the user: the handwritten signature or the initials used for "ký nháy".</summary>
public enum SignatureImageKind
{
    Signature = 0,
    Initial = 1,
}

/// <summary>
/// Images drawn in the visible signature (spec §8): each user's handwritten signature and initials, the organisation
/// seal for "đóng dấu điện tử". Stored in object storage; the user table / organisation keep the key.
/// </summary>
public sealed class SignatureImageService(
    IAppDbContext db,
    IFileStorage storage,
    IPdfSignatureService pdf,
    ICurrentUser current,
    IAuditLogger audit)
{
    public const long MaxBytes = 2 * 1024 * 1024;

    public async Task<Result> SetMineAsync(SignatureImageKind kind, UploadedFile file, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == current.UserId, ct);
        if (user is null)
        {
            return Result.Failure(Error.NotFound());
        }

        var stored = await StoreAsync($"users/{user.Id:N}/{(kind == SignatureImageKind.Initial ? "initial" : "signature")}", file, ct);
        if (!stored.IsSuccess)
        {
            return Result.Failure(stored.Error!);
        }

        user.SetSignatureImages(
            kind == SignatureImageKind.Signature ? stored.Value : user.SignatureImageFileKey,
            kind == SignatureImageKind.Initial ? stored.Value : user.InitialImageFileKey);
        audit.Log("SignatureImageSet", "User", user.Id.ToString(), new { kind = kind.ToString() });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveMineAsync(SignatureImageKind kind, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == current.UserId, ct);
        if (user is null)
        {
            return Result.Failure(Error.NotFound());
        }

        user.SetSignatureImages(
            kind == SignatureImageKind.Signature ? null : user.SignatureImageFileKey,
            kind == SignatureImageKind.Initial ? null : user.InitialImageFileKey);
        audit.Log("SignatureImageRemove", "User", user.Id.ToString(), new { kind = kind.ToString() });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<(byte[] Content, string ContentType)>> GetMineAsync(SignatureImageKind kind, CancellationToken ct)
    {
        var key = await db.Users.AsNoTracking().Where(u => u.Id == current.UserId)
            .Select(u => kind == SignatureImageKind.Initial ? u.InitialImageFileKey : u.SignatureImageFileKey)
            .FirstOrDefaultAsync(ct);
        return await ReadAsync(key, ct);
    }

    public async Task<Result> SetSealAsync(UploadedFile file, CancellationToken ct)
    {
        var organization = await db.Organizations.FirstOrDefaultAsync(ct);
        if (organization is null)
        {
            return Result.Failure(Error.NotFound());
        }

        var stored = await StoreAsync($"organization/{organization.Id:N}/seal", file, ct);
        if (!stored.IsSuccess)
        {
            return Result.Failure(stored.Error!);
        }

        organization.SetSealImage(stored.Value);
        audit.Log("SealImageSet", "Organization", organization.Id.ToString(), null);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveSealAsync(CancellationToken ct)
    {
        var organization = await db.Organizations.FirstOrDefaultAsync(ct);
        if (organization is null)
        {
            return Result.Failure(Error.NotFound());
        }

        organization.SetSealImage(null);
        audit.Log("SealImageRemove", "Organization", organization.Id.ToString(), null);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<(byte[] Content, string ContentType)>> GetSealAsync(CancellationToken ct) =>
        await ReadAsync(await db.Organizations.AsNoTracking().Select(o => o.SealImageFileKey).FirstOrDefaultAsync(ct), ct);

    /// <summary>The image to draw for a signature of this kind by this user, or null for a text-only appearance.</summary>
    public async Task<byte[]?> ForSignatureAsync(SignatureKind kind, Guid userId, CancellationToken ct)
    {
        string? key;
        if (kind == SignatureKind.Organization)
        {
            key = await db.Organizations.AsNoTracking().Select(o => o.SealImageFileKey).FirstOrDefaultAsync(ct);
        }
        else
        {
            var user = await db.Users.AsNoTracking().IgnoreQueryFilters().Where(u => u.Id == userId)
                .Select(u => new { u.SignatureImageFileKey, u.InitialImageFileKey }).FirstOrDefaultAsync(ct);
            key = kind == SignatureKind.Initial ? user?.InitialImageFileKey ?? user?.SignatureImageFileKey : user?.SignatureImageFileKey;
        }

        var image = await ReadAsync(key, ct);
        return image.IsSuccess ? image.Value.Content : null;
    }

    private async Task<Result<string>> StoreAsync(string prefix, UploadedFile file, CancellationToken ct)
    {
        var kind = FileInspector.Detect(file.Content);
        if (kind is not (FileKind.Png or FileKind.Jpeg) || !pdf.IsUsableImage(file.Content))
        {
            return Error.Validation("SIGNATURE_IMAGE_INVALID");
        }

        if (file.Content.LongLength > MaxBytes)
        {
            return Error.Validation("SIGNATURE_IMAGE_TOO_LARGE");
        }

        var (mediaType, extension) = FileInspector.Storage(kind);
        var key = $"{prefix}-{Guid.CreateVersion7():N}{extension}";
        using var stream = new MemoryStream(file.Content, writable: false);
        await storage.UploadAsync(key, stream, mediaType, ct);
        return key;
    }

    private async Task<Result<(byte[] Content, string ContentType)>> ReadAsync(string? key, CancellationToken ct)
    {
        if (key is null)
        {
            return Error.NotFound("SIGNATURE_IMAGE_NOT_SET");
        }

        await using var stream = await storage.DownloadAsync(key, ct);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        var content = memory.ToArray();
        return (content, FileInspector.Storage(FileInspector.Detect(content)).MediaType);
    }
}
