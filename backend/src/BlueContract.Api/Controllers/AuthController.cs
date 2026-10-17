using BlueContract.Api.Auth;
using BlueContract.Api.Middleware;
using BlueContract.Application.Auth;
using BlueContract.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlueContract.Api.Controllers;

[Route("api/auth")]
[AllowWithPendingPasswordChange]
public sealed class AuthController(AuthService auth, ICurrentUser currentUser, IWebHostEnvironment environment) : ApiControllerBase
{
    /// <summary>httpOnly, SameSite=Strict, scoped to the auth endpoints so it is never sent elsewhere (spec §11).</summary>
    public const string RefreshCookie = "bp_refresh";
    private const string CookiePath = "/api/auth";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthSetup.LoginRateLimit)]
    public async Task<ActionResult<LoginResult>> Login(LoginRequest request, CancellationToken ct) =>
        Issue(await auth.LoginAsync(request, ct));

    [HttpPost("login/2fa")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthSetup.LoginRateLimit)]
    public async Task<ActionResult<LoginResult>> LoginTwoFactor(TwoFactorLoginRequest request, CancellationToken ct) =>
        Issue(await auth.LoginWithTwoFactorAsync(request, ct));

    /// <summary>Rotates the refresh cookie and returns a new access token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResult>> Refresh(CancellationToken ct)
    {
        var result = await auth.RefreshAsync(Request.Cookies[RefreshCookie], ct);
        if (!result.IsSuccess)
        {
            ClearCookie();
        }

        return Issue(result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        ClearCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        var me = await auth.GetCurrentUserAsync(currentUser.UserId!.Value, ct);
        return me is null ? Problem(Error.Unauthorized()) : me;
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await auth.ChangePasswordAsync(request, ct);
        if (result.IsSuccess)
        {
            AuthSetup.InvalidateStamp(HttpContext.RequestServices, currentUser.UserId!.Value);
            ClearCookie();
        }

        return FromResult(result);
    }

    [HttpPost("2fa/setup")]
    [Authorize]
    public async Task<ActionResult<TwoFactorSetupResponse>> SetupTwoFactor(CancellationToken ct) =>
        FromResult(await auth.BeginTwoFactorSetupAsync("BlueProcure", ct));

    [HttpPost("2fa/enable")]
    [Authorize]
    public async Task<IActionResult> EnableTwoFactor(EnableTwoFactorRequest request, CancellationToken ct) =>
        FromResult(await auth.EnableTwoFactorAsync(request, ct));

    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> DisableTwoFactor(DisableTwoFactorRequest request, CancellationToken ct) =>
        FromResult(await auth.DisableTwoFactorAsync(request, ct));

    private ActionResult<LoginResult> Issue(Result<LoginResult> result)
    {
        if (!result.IsSuccess)
        {
            return Problem(result.Error!);
        }

        if (result.Value.GetRefreshCookie() is { } cookie)
        {
            Response.Cookies.Append(RefreshCookie, cookie.Token, new CookieOptions
            {
                HttpOnly = true,
                // HTTPS in production (the proxies forward the scheme); always Secure there, even if a proxy hop forgot
                // X-Forwarded-Proto. Plain HTTP only in local dev and tests.
                Secure = Request.IsHttps || environment.IsProduction(),
                SameSite = SameSiteMode.Strict,
                Path = CookiePath,
                Expires = cookie.ExpiresAt,
                IsEssential = true,
            });
        }

        return result.Value;
    }

    private void ClearCookie() => Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = CookiePath });
}
