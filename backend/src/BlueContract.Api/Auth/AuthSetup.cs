using System.Threading.RateLimiting;
using BlueContract.Application.Auth;
using BlueContract.Application.Common;
using BlueContract.Infrastructure.Identity;
using BlueContract.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace BlueContract.Api.Auth;

internal static class AuthSetup
{
    public const string LoginRateLimit = "login";

    public static IServiceCollection AddAppAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = auth.Issuer,
                    ValidAudience = auth.Audience,
                    IssuerSigningKey = string.IsNullOrEmpty(auth.SigningKey) ? null : JwtTokenService.CreateKey(auth),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AppClaims.UserName,
                };
                options.Events = new JwtBearerEvents
                {
                    // SignalR sends the token in the query string for WebSocket connections.
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs", StringComparison.Ordinal))
                        {
                            ctx.Token = token;
                        }

                        return Task.CompletedTask;
                    },

                    // Reject tokens whose security stamp no longer matches (password/role change, lockout, deactivation).
                    OnTokenValidated = async ctx =>
                    {
                        var sub = ctx.Principal?.FindFirst(AppClaims.UserId)?.Value;
                        var stamp = ctx.Principal?.FindFirst(AppClaims.SecurityStamp)?.Value;
                        if (!Guid.TryParse(sub, out var userId) || stamp is null)
                        {
                            ctx.Fail("invalid_token");
                            return;
                        }

                        var current = await GetStampAsync(ctx.HttpContext.RequestServices, userId, ctx.HttpContext.RequestAborted);
                        if (current != stamp)
                        {
                            ctx.Fail("stale_token");
                        }
                    },
                    OnChallenge = async ctx =>
                    {
                        ctx.HandleResponse();
                        await WriteProblemAsync(ctx.HttpContext, StatusCodes.Status401Unauthorized, "UNAUTHORIZED");
                    },
                    OnForbidden = ctx => WriteProblemAsync(ctx.HttpContext, StatusCodes.Status403Forbidden, "FORBIDDEN"),
                };
            });

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (ctx, _) => new ValueTask(WriteProblemAsync(ctx.HttpContext, StatusCodes.Status429TooManyRequests, "TOO_MANY_REQUESTS"));
            options.AddPolicy(LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = auth.LoginRateLimitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        return services;
    }

    /// <summary>Clears the cached stamp so the next request re-reads it (used right after the stamp changes).</summary>
    public static void InvalidateStamp(IServiceProvider services, Guid userId) =>
        services.GetRequiredService<IMemoryCache>().Remove(StampCacheKey(userId));

    private static async Task<string?> GetStampAsync(IServiceProvider services, Guid userId, CancellationToken ct)
    {
        var cache = services.GetRequiredService<IMemoryCache>();
        if (cache.TryGetValue(StampCacheKey(userId), out string? cached))
        {
            return cached;
        }

        var db = services.GetRequiredService<AppDbContext>();
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeleted && u.Status == Domain.Identity.UserStatus.Active)
            .Select(u => new { u.SecurityStamp })
            .FirstOrDefaultAsync(ct);
        var stamp = user?.SecurityStamp;

        // Short cache: revocations take effect within seconds without a DB hit per request.
        cache.Set(StampCacheKey(userId), stamp, TimeSpan.FromSeconds(15));
        return stamp;
    }

    private static string StampCacheKey(Guid userId) => "stamp:" + userId.ToString("N");

    private static Task WriteProblemAsync(HttpContext http, int status, string code)
    {
        http.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = ErrorMessages.Get(code), Type = $"https://blueprocure.local/errors/{code}" };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = http.TraceIdentifier;
        return http.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem }).AsTask();
    }
}
