using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlueContract.Application.Documents;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BlueContract.Infrastructure.Documents;

public sealed class OnlyOfficeOptions
{
    public const string SectionName = "OnlyOffice";

    /// <summary>URL the browser uses to load the editor (e.g. http://localhost:47082).</summary>
    public Uri PublicUrl { get; set; } = new("http://localhost:47082");

    /// <summary>URL the API uses to reach the document server (rewrites download links in callbacks).</summary>
    public Uri InternalUrl { get; set; } = new("http://localhost:47082");

    /// <summary>Base URL at which the document server reaches this API (file download + callback).</summary>
    public Uri ApiBaseUrlForEditor { get; set; } = new("http://api:8080");

    /// <summary>Shared JWT secret (JWT_SECRET of the document server).</summary>
    public string JwtSecret { get; set; } = string.Empty;
}

/// <summary>
/// ONLYOFFICE Docs integration (spec §2): signed editor config, forced track-changes for supplier review,
/// JWT-verified save callbacks. Community edition licence notes in docs/DECISIONS.md (ADR-009).
/// </summary>
internal sealed class OnlyOfficeEditorService(IOptions<OnlyOfficeOptions> options, IHttpClientFactory httpClientFactory) : IDocumentEditorService
{
    private readonly OnlyOfficeOptions _options = options.Value;
    private readonly JsonWebTokenHandler _jwt = new();

    public Uri EditorScriptBaseUrl => _options.PublicUrl;

    public EditorConfig BuildConfig(EditorConfigRequest r)
    {
        var review = r.Mode == EditorMode.Review;
        var edit = r.Mode == EditorMode.Edit;
        var config = new JsonObject
        {
            ["documentType"] = "word",
            ["type"] = "desktop",
            ["width"] = "100%",
            ["height"] = "100%",
            ["document"] = new JsonObject
            {
                ["fileType"] = "docx",
                ["key"] = r.SessionKey,
                ["title"] = r.Title.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? r.Title : r.Title + ".docx",
                ["url"] = r.DownloadUrl.ToString(),
                ["permissions"] = new JsonObject
                {
                    // Supplier review: no direct editing, only tracked suggestions.
                    ["edit"] = edit,
                    ["review"] = edit || review,
                    ["comment"] = r.Mode != EditorMode.View,
                    ["download"] = true,
                    ["print"] = true,
                },
            },
            ["editorConfig"] = new JsonObject
            {
                ["mode"] = r.Mode == EditorMode.View ? "view" : "edit",
                ["lang"] = "vi",
                ["region"] = "vi-VN",
                ["callbackUrl"] = r.CallbackUrl.ToString(),
                ["user"] = new JsonObject { ["id"] = r.UserId, ["name"] = r.UserName },
                ["customization"] = new JsonObject
                {
                    ["forcesave"] = true,
                    ["autosave"] = true,
                    ["compactHeader"] = true,
                    ["review"] = new JsonObject
                    {
                        // Deleted text struck through, inserted text highlighted (spec §6.1 B3.3).
                        ["trackChanges"] = review ? true : (JsonNode?)null,
                        ["showReviewChanges"] = true,
                        ["reviewDisplay"] = "markup",
                    },
                },
            },
        };

        if (!string.IsNullOrEmpty(_options.JwtSecret))
        {
            config["token"] = Sign(config);
        }

        return new EditorConfig(config);
    }

    public EditorCallback? ParseCallback(string body, string? authorizationHeader)
    {
        JsonObject? payload;
        if (!string.IsNullOrEmpty(_options.JwtSecret))
        {
            // The document server sends the payload as a JWT in the body ("token") and/or the Authorization header.
            var token = JsonNode.Parse(body)?["token"]?.GetValue<string>()
                ?? authorizationHeader?.Replace("Bearer ", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var result = _jwt.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                IssuerSigningKey = Key(),
            }).GetAwaiter().GetResult();
            if (!result.IsValid)
            {
                return null;
            }

            var json = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]));
            payload = JsonNode.Parse(json) as JsonObject;

            // Header tokens wrap the callback in "payload".
            if (payload?["payload"] is JsonObject inner)
            {
                payload = inner;
            }
        }
        else
        {
            payload = JsonNode.Parse(body) as JsonObject;
        }

        if (payload?["key"] is not JsonValue keyNode || payload["status"] is not JsonValue statusNode)
        {
            return null;
        }

        var url = payload["url"]?.GetValue<string>();
        var users = payload["users"] is JsonArray arr ? arr.Select(u => u?.ToString() ?? string.Empty).ToList() : [];
        return new EditorCallback(keyNode.ToString(), statusNode.GetValue<int>(), url is null ? null : RewriteToInternal(new Uri(url)), users);
    }

    public async Task<byte[]> DownloadEditedFileAsync(Uri url, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient(DependencyInjection.OnlyOfficeClient);
        client.Timeout = TimeSpan.FromSeconds(60);
        return await client.GetByteArrayAsync(url, ct);
    }

    /// <summary>The document server builds download links with the browser-facing host; the API must use the internal one.</summary>
    private Uri RewriteToInternal(Uri url)
    {
        if (url.Host == _options.InternalUrl.Host && url.Port == _options.InternalUrl.Port)
        {
            return url;
        }

        var builder = new UriBuilder(url) { Scheme = _options.InternalUrl.Scheme, Host = _options.InternalUrl.Host, Port = _options.InternalUrl.Port };
        return builder.Uri;
    }

    private string Sign(JsonObject config)
    {
        var claims = JsonSerializer.Deserialize<Dictionary<string, object>>(config.ToJsonString())!;
        return _jwt.CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            SigningCredentials = new SigningCredentials(Key(), SecurityAlgorithms.HmacSha256),
            Expires = DateTime.UtcNow.AddHours(8),
            Subject = new ClaimsIdentity(),
        });
    }

    private SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(_options.JwtSecret));
}
