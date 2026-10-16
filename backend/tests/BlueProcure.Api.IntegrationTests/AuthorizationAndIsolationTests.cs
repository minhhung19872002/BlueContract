using System.Net;
using System.Net.Http.Json;
using BlueProcure.Api.IntegrationTests.Infrastructure;
using BlueProcure.Application.Admin;
using BlueProcure.Application.Common;
using BlueProcure.Domain.Identity;
using BlueProcure.Domain.Organizations;
using BlueProcure.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueProcure.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthorizationAndIsolationTests(ApiFactory factory)
{
    [Fact]
    public async Task Anonymous_requests_get_401_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/admin/users", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Theory]
    [InlineData("khoanoi", "/api/admin/users")]
    [InlineData("muasam", "/api/admin/roles/matrix")]
    [InlineData("ncc1", "/api/admin/suppliers")]
    [InlineData("ncc1.user", "/api/supplier/company/staff/00000000-0000-0000-0000-000000000000")]
    public async Task Missing_permission_returns_403(string userName, string url)
    {
        using var client = await factory.LoginAsync(userName);

        using var response = url.Contains("/staff/", StringComparison.Ordinal)
            ? await client.PutAsJsonAsync(url, new UpdateSupplierStaffRequest(UserStatus.Active, false), JsonDefaults.Options)
            : await client.GetAsync(new Uri(url, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Supplier_sees_only_its_own_supplier_in_lookups()
    {
        using var client = await factory.LoginAsync("ncc1");

        var suppliers = await client.GetFromJsonAsync<List<LookupItem>>("/api/lookups/suppliers");

        suppliers!.Count.ShouldBe(1);
        suppliers[0].Code.ShouldBe("0101234567");
    }

    [Fact]
    public async Task Supplier_staff_list_contains_only_own_company()
    {
        using var client = await factory.LoginAsync("ncc1");

        var staff = await client.GetFromJsonAsync<List<SupplierStaffDto>>("/api/supplier/company/staff", JsonDefaults.Options);

        staff!.Select(s => s.UserName).Order().ShouldBe(["ncc1", "ncc1.user"]);
    }

    [Fact]
    public async Task Supplier_cannot_modify_another_suppliers_user_even_with_guessed_id()
    {
        var ncc2UserId = await IdOfUserAsync("ncc2.user");
        using var client = await factory.LoginAsync("ncc1");

        using var response = await client.PutAsJsonAsync(
            $"/api/supplier/company/staff/{ncc2UserId}", new UpdateSupplierStaffRequest(UserStatus.Inactive, false), JsonDefaults.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == ncc2UserId)).Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public async Task Query_filter_hides_other_suppliers_rows_at_the_data_layer()
    {
        using var scope = factory.Services.CreateScope();
        var lookup = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ncc1 = await lookup.Suppliers.FirstAsync(s => s.TaxCode == "0101234567");
        var ncc2 = await lookup.Suppliers.FirstAsync(s => s.TaxCode == "0302345678");

        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        await using var asSupplier = new AppDbContext(options, new FakeCurrentUser(UserType.Supplier, ncc1.Id));

        (await asSupplier.Suppliers.FirstOrDefaultAsync(s => s.Id == ncc2.Id)).ShouldBeNull();
        (await asSupplier.Suppliers.FirstOrDefaultAsync(s => s.Id == ncc1.Id)).ShouldNotBeNull();
        (await asSupplier.Users.AnyAsync(u => u.SupplierId == ncc2.Id)).ShouldBeFalse();
        (await asSupplier.Users.AnyAsync(u => u.UserType == UserType.Internal)).ShouldBeFalse();

        await using var asInternal = new AppDbContext(options, new FakeCurrentUser(UserType.Internal, null));
        (await asInternal.Suppliers.CountAsync()).ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Permission_matrix_lists_all_roles_and_permissions()
    {
        using var client = await factory.LoginAsync("admin");

        var matrix = await client.GetFromJsonAsync<PermissionMatrixDto>("/api/admin/roles/matrix", JsonDefaults.Options);

        RoleCatalog.All.Select(r => r.Code).Except(matrix!.Roles.Select(r => r.Code)).ShouldBeEmpty();
        matrix.Permissions.Count.ShouldBe(PermissionCatalog.All.Count);
    }

    [Fact]
    public async Task Removing_a_permission_takes_effect_for_existing_sessions()
    {
        using var admin = await factory.LoginAsync("admin");
        var matrix = await admin.GetFromJsonAsync<PermissionMatrixDto>("/api/admin/roles/matrix", JsonDefaults.Options);
        var viewer = matrix!.Roles.Single(r => r.Code == RoleCatalog.Viewer);
        using var viewerClient = await factory.LoginAsync("xem");
        (await viewerClient.GetAsync(new Uri("/api/lookups/facilities", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        try
        {
            using var set = await admin.PutAsJsonAsync($"/api/admin/roles/{viewer.Id}/permissions",
                new SetRolePermissionsRequest([Permissions.ContractView]));
            set.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // Stamp rotated → old token rejected (cache TTL is short; poll briefly).
            var status = HttpStatusCode.OK;
            for (var i = 0; i < 40 && status == HttpStatusCode.OK; i++)
            {
                status = (await viewerClient.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode;
                if (status == HttpStatusCode.OK)
                {
                    await Task.Delay(500);
                }
            }

            status.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            using var restore = await admin.PutAsJsonAsync($"/api/admin/roles/{viewer.Id}/permissions",
                new SetRolePermissionsRequest(viewer.Permissions));
        }
    }

    private async Task<Guid> IdOfUserAsync(string userName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.IgnoreQueryFilters().Where(u => u.UserName == userName).Select(u => u.Id).FirstAsync();
    }

    private sealed class FakeCurrentUser(UserType type, Guid? supplierId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid? UserId => Guid.Empty;

        public string? UserName => "test";

        public string? FullName => "test";

        public UserType? UserType => type;

        public Guid? SupplierId => supplierId;

        public IReadOnlySet<string> Permissions => new HashSet<string>();

        public IReadOnlyList<(DataScopeType Type, string Value)> DataScopes => [];

        public string? IpAddress => null;

        public string? UserAgent => null;

        public string? CorrelationId => null;
    }
}
