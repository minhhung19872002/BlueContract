using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Catalog;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Contracts;

/// <summary>
/// AUDIT-1 §11.9 / spec §3 "FacilityId(s)": a contract is carried out at one, several or every facility (empty list);
/// a user scoped to a facility sees the contracts of that facility and the hospital-wide ones, not the others.
/// </summary>
[Collection(ApiCollection.Name)]
public class ContractFacilitiesTests(ApiFactory factory)
{
    [Fact]
    public async Task A_contract_covers_several_facilities_and_data_scope_follows_them()
    {
        using var buyer = await factory.LoginAsync("muasam");
        var facilities = (await buyer.GetFromJsonAsync<List<LookupItem>>("/api/lookups/facilities", JsonDefaults.Options))!;
        var cs2 = facilities.Single(f => f.Code == "CS2");
        var cs3 = facilities.Single(f => f.Code == "CS3");
        var yc = facilities.Single(f => f.Code == "YC");

        var id = await CreateAsync(buyer, [cs2.Id, cs3.Id, cs2.Id]);
        var contract = await buyer.GetFromJsonAsync<JsonElement>($"/api/contracts/{id}");
        contract.GetProperty("facilityIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([cs2.Id, cs3.Id], ignoreOrder: true);
        contract.GetProperty("facilityNames").EnumerateArray().Select(e => e.GetString()).ShouldBe([cs2.Name, cs3.Name], ignoreOrder: true);

        // An unknown facility is refused.
        using var unknown = await PostAsync(buyer, [Guid.NewGuid()]);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var hospitalWide = await CreateAsync(buyer, []);
        (await buyer.GetFromJsonAsync<JsonElement>($"/api/contracts/{hospitalWide}")).GetProperty("facilityIds").GetArrayLength().ShouldBe(0);

        try
        {
            await SetFacilityScopeAsync("xem", cs3.Id);
            using (var viewer = await factory.LoginAsync("xem"))
            {
                (await viewer.GetAsync(new Uri($"/api/contracts/{id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
                (await viewer.GetAsync(new Uri($"/api/contracts/{hospitalWide}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            await SetFacilityScopeAsync("xem", yc.Id);
            using (var viewer = await factory.LoginAsync("xem"))
            {
                (await viewer.GetAsync(new Uri($"/api/contracts/{id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
                (await viewer.GetAsync(new Uri($"/api/contracts/{hospitalWide}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
            }
        }
        finally
        {
            await SetFacilityScopeAsync("xem", null);
        }
    }

    private async Task SetFacilityScopeAsync(string userName, Guid? facilityId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM user_data_scopes WHERE user_id = {userId}");
        if (facilityId is { } fid)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO user_data_scopes (id, user_id, scope_type, value) VALUES ({Guid.CreateVersion7()}, {userId}, {nameof(DataScopeType.Facility)}, {fid.ToString()})");
        }
    }

    private static async Task<Guid> CreateAsync(HttpClient buyer, Guid[] facilityIds)
    {
        using var created = await PostAsync(buyer, facilityIds);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    /// <summary>Raw JSON body, so the test also compiles against the single-facility request.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient buyer, Guid[] facilityIds)
    {
        var categories = await buyer.GetFromJsonAsync<List<ItemCategoryDto>>("/api/items/categories", JsonDefaults.Options);
        var templates = await buyer.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates?kind=Contract", JsonDefaults.Options);
        var suppliers = await buyer.GetFromJsonAsync<List<LookupItem>>("/api/lookups/suppliers?search=Hồng Lam", JsonDefaults.Options);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        return await buyer.PostAsJsonAsync("/api/contracts", new
        {
            procurementType = "NonBidding",
            supplierId = suppliers!.First().Id,
            number = $"CS-{tag}/2026/HĐ",
            name = "Bảo trì thiết bị nhiều cơ sở",
            kind = "LumpSum",
            requiresWarehouseReceipt = true,
            categoryId = categories!.Single(c => c.Code == "SUA_CHUA").Id,
            facilityIds,
            signedDate = today,
            effectiveDate = today,
            expiryDate = today.AddMonths(3),
            templateId = templates!.Single(t => t.Code == "HD_DICHVU_KHONGTHAU").Id,
            items = new[]
            {
                new { newItem = new { code = $"CS-{tag}", name = "Bảo trì tháng", unit = "Lần" }, quantity = 1, unitPrice = 1_000_000, vatRate = 8 },
            },
        });
    }
}
