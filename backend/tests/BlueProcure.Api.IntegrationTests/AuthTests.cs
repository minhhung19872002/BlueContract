using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueProcure.Api.IntegrationTests.Infrastructure;
using BlueProcure.Application.Admin;
using BlueProcure.Application.Auth;
using BlueProcure.Domain.Identity;
using BlueProcure.Infrastructure.Identity;
using BlueProcure.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueProcure.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Theory]
    [InlineData("admin", RoleCatalog.SysAdmin)]
    [InlineData("muasam", RoleCatalog.Procurement)]
    [InlineData("rasoat", RoleCatalog.Reviewer)]
    [InlineData("khoanoi", RoleCatalog.DeptUser)]
    [InlineData("tonghop", RoleCatalog.Aggregator)]
    [InlineData("totruongduoc", RoleCatalog.DomainHead)]
    [InlineData("ketoan", RoleCatalog.Accountant)]
    [InlineData("ketoantruong", RoleCatalog.ChiefAccountant)]
    [InlineData("ketoankho", RoleCatalog.WarehouseAccountant)]
    [InlineData("thukho", RoleCatalog.Storekeeper)]
    [InlineData("truongkhoa", RoleCatalog.DeptHead)]
    [InlineData("giamdoc", RoleCatalog.Director)]
    [InlineData("vanthu", RoleCatalog.Clerk)]
    [InlineData("ncc1", RoleCatalog.SupplierAdmin)]
    [InlineData("ncc1.user", RoleCatalog.SupplierUser)]
    public async Task Every_demo_account_logs_in_with_its_role(string userName, string role)
    {
        using var client = await factory.LoginAsync(userName);

        var me = await client.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", JsonDefaults.Options);

        me!.UserName.ShouldBe(userName);
        me.Roles.ShouldContain(role);
        me.Permissions.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Wrong_password_returns_vietnamese_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("muasam", "sai-mat-khau"));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        problem.GetProperty("code").GetString().ShouldBe("INVALID_CREDENTIALS");
        problem.GetProperty("title").GetString().ShouldBe("Tên đăng nhập hoặc mật khẩu không đúng.");
    }

    [Fact]
    public async Task Account_locks_after_five_failed_attempts()
    {
        await CreateUserAsync("lockme", "Lock@1234");
        using var client = factory.CreateClient();

        for (var i = 0; i < 4; i++)
        {
            using var r = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("lockme", "wrong"));
            (await Code(r)).ShouldBe("INVALID_CREDENTIALS");
        }

        using var fifth = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("lockme", "wrong"));
        (await Code(fifth)).ShouldBe("ACCOUNT_LOCKED");

        // Even the right password is refused while locked.
        using var correct = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("lockme", "Lock@1234"));
        (await Code(correct)).ShouldBe("ACCOUNT_LOCKED");
    }

    [Fact]
    public async Task Refresh_rotates_token_and_reuse_kills_the_session()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("ketoan", ApiFactory.DemoPassword));
        var firstCookie = RefreshCookie(login);
        firstCookie.ShouldNotBeNull();

        using var refresh1 = await PostWithCookie(client, "/api/auth/refresh", firstCookie);
        refresh1.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondCookie = RefreshCookie(refresh1);
        secondCookie.ShouldNotBe(firstCookie);

        // Presenting the already-rotated token is treated as theft…
        using var reuse = await PostWithCookie(client, "/api/auth/refresh", firstCookie);
        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // …and revokes the newer token of the same family too.
        using var afterReuse = await PostWithCookie(client, "/api/auth/refresh", secondCookie!);
        afterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_cookie_is_httponly_and_samesite_strict()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("ketoan", ApiFactory.DemoPassword));

        var header = login.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("bp_refresh=", StringComparison.Ordinal));
        header.ShouldContain("httponly", Case.Insensitive);
        header.ShouldContain("samesite=strict", Case.Insensitive);
        header.ShouldContain("path=/api/auth", Case.Insensitive);
        (await login.Content.ReadAsStringAsync()).ShouldNotContain("refreshToken", Case.Insensitive);
    }

    [Fact]
    public async Task Two_factor_login_requires_valid_totp()
    {
        await CreateUserAsync("twofa", "TwoFa@1234");
        using var client = await factory.LoginAsync("twofa", "TwoFa@1234");

        using var setup = await client.PostAsync("/api/auth/2fa/setup", null);
        var secret = (await setup.Content.ReadFromJsonAsync<TwoFactorSetupResponse>())!.Secret;
        var code = TotpService.Compute(TotpService.FromBase32(secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        using var enable = await client.PostAsJsonAsync("/api/auth/2fa/enable", new EnableTwoFactorRequest(code));
        enable.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var anonymous = factory.CreateClient();
        using var step1 = await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest("twofa", "TwoFa@1234"));
        var challenge = await step1.Content.ReadFromJsonAsync<LoginResult>(JsonDefaults.Options);
        challenge!.RequiresTwoFactor.ShouldBeTrue();
        challenge.AccessToken.ShouldBeNull();

        using var wrong = await anonymous.PostAsJsonAsync("/api/auth/login/2fa", new TwoFactorLoginRequest(challenge.TwoFactorChallenge!, "000000"));
        (await Code(wrong)).ShouldBe("TWO_FACTOR_INVALID");

        var fresh = TotpService.Compute(TotpService.FromBase32(secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        using var ok = await anonymous.PostAsJsonAsync("/api/auth/login/2fa", new TwoFactorLoginRequest(challenge.TwoFactorChallenge!, fresh));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<LoginResult>(JsonDefaults.Options))!.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task New_user_must_change_password_before_using_the_system()
    {
        await CreateUserAsync("newbie", "Newbie@1234", mustChange: true);
        using var client = await factory.LoginAsync("newbie", "Newbie@1234");

        using var blocked = await client.GetAsync(new Uri("/api/lookups/facilities", UriKind.Relative));
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(blocked)).ShouldBe("PASSWORD_CHANGE_REQUIRED");

        using var change = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Newbie@1234", "Moi@12345678"));
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var relogged = await factory.LoginAsync("newbie", "Moi@12345678");
        using var allowed = await relogged.GetAsync(new Uri("/api/lookups/facilities", UriKind.Relative));
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Weak_new_password_is_rejected_with_field_error()
    {
        using var client = await factory.LoginAsync("rasoat");

        using var response = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(ApiFactory.DemoPassword, "123"));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("newPassword", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Deactivated_user_token_is_rejected_immediately()
    {
        var userId = await CreateUserAsync("deactivate", "Deact@1234");
        using var client = await factory.LoginAsync("deactivate", "Deact@1234");
        (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var admin = await factory.LoginAsync("admin");
        var detail = await admin.GetFromJsonAsync<UserDetailDto>($"/api/admin/users/{userId}", JsonDefaults.Options);
        using var update = await admin.PutAsJsonAsync($"/api/admin/users/{userId}", new UpdateUserRequest(
            detail!.FullName, detail.Email, detail.Phone, detail.JobTitle, detail.DepartmentId, detail.FacilityId, UserStatus.Inactive,
            detail.RoleIds, detail.DataScopes, null, null, null), JsonDefaults.Options);
        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> CreateUserAsync(string userName, string password, bool mustChange = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var existing = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserName == userName);
        if (existing is not null)
        {
            return existing.Id;
        }

        var roleId = await db.Roles.Where(r => r.Code == RoleCatalog.Viewer).Select(r => r.Id).FirstAsync();
        var user = new User(userName, "Người dùng thử " + userName, null, UserType.Internal, null);
        user.SetPasswordHash(passwords.Hash(user, password), mustChange);
        user.SetRoles([roleId]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string? RefreshCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(v => v.StartsWith("bp_refresh=", StringComparison.Ordinal))
                .Select(v => v.Split(';')[0])
                .FirstOrDefault(v => v.Length > "bp_refresh=".Length)
            : null;

    private static Task<HttpResponseMessage> PostWithCookie(HttpClient client, string url, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
    }
}
