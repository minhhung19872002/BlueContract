using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using BlueContract.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlueContract.Infrastructure.Integration;

/// <summary>
/// Access token of the HIS service account (His:AuthMode=Login), shared by every HIS call of this instance: one login
/// per <see cref="HisOptions.TokenLifetimeMinutes"/>, not one per request (vendors rate-limit their login endpoint, and a
/// "last login wins" session policy would make parallel logins revoke each other).
/// </summary>
internal sealed class HisTokenCache(IClock clock) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? token;
    private DateTimeOffset expiresAt;

    public async Task<string> GetAsync(Func<CancellationToken, Task<(string Token, TimeSpan Lifetime)>> login, string? rejected, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            // Another request may already have replaced the token the caller saw rejected.
            if (token is not null && token != rejected && clock.UtcNow < expiresAt)
            {
                return token;
            }

            var (fresh, lifetime) = await login(ct);
            (token, expiresAt) = (fresh, clock.UtcNow + lifetime);
            return fresh;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}

/// <summary>Adds the service account's bearer token to HIS calls; on 401 logs in again and retries once.</summary>
internal sealed class HisLoginHandler(HisTokenCache cache, IOptions<HisOptions> options, ILogger<HisLoginHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await cache.GetAsync(ct => LoginAsync(request.RequestUri!, ct), null, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || request.Content is not null)
        {
            return response;
        }

        // Token revoked or expired early (HIS restart, password rotation): one fresh login, one retry (GETs only).
        response.Dispose();
        token = await cache.GetAsync(ct => LoginAsync(request.RequestUri!, ct), token, cancellationToken);
        using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(retry, cancellationToken);
    }

    private async Task<(string Token, TimeSpan Lifetime)> LoginAsync(Uri sample, CancellationToken ct)
    {
        var o = options.Value;
        var baseUri = new Uri(o.BaseUrl!.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/");
        using var login = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, o.LoginPath.TrimStart('/')))
        {
            Content = JsonContent.Create(new { username = o.Username, password = o.Password }),
        };
        using var response = await base.SendAsync(login, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("HIS login as {User} failed with {Status} (calling {Url})", o.Username, (int)response.StatusCode, sample);
            throw new HttpRequestException($"HIS login failed ({(int)response.StatusCode}).", null, response.StatusCode);
        }

        var node = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        foreach (var part in o.TokenPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node is JsonObject obj && obj.TryGetPropertyValue(part, out var next) ? next : null;
        }

        var token = node?.ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            // e.g. the account has 2FA on: the HIS answers 200 with an OTP challenge and no token.
            throw new HttpRequestException($"HIS login returned no token at '{o.TokenPath}'.");
        }

        return (token, TimeSpan.FromMinutes(Math.Max(o.TokenLifetimeMinutes, 1)));
    }
}
