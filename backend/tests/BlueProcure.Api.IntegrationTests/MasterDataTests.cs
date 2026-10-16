using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueProcure.Api.IntegrationTests.Infrastructure;
using BlueProcure.Application.Admin;
using BlueProcure.Application.Common;
using BlueProcure.Domain.Common;
using BlueProcure.Domain.Organizations;
using BlueProcure.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace BlueProcure.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MasterDataTests(ApiFactory factory)
{
    [Fact]
    public async Task Demo_seed_has_expected_master_data()
    {
        using var client = await factory.LoginAsync("admin");

        var facilities = await client.GetFromJsonAsync<PagedResult<FacilityDto>>("/api/admin/facilities?pageSize=50", JsonDefaults.Options);
        var departments = await client.GetFromJsonAsync<PagedResult<DepartmentDto>>("/api/admin/departments?pageSize=50", JsonDefaults.Options);
        var warehouses = await client.GetFromJsonAsync<PagedResult<WarehouseDto>>("/api/admin/warehouses?pageSize=50", JsonDefaults.Options);
        var org = await client.GetFromJsonAsync<OrganizationDto>("/api/admin/organization", JsonDefaults.Options);

        facilities!.Total.ShouldBe(4);
        departments!.Total.ShouldBeGreaterThanOrEqualTo(15);
        warehouses!.Total.ShouldBe(5);
        org!.Name.ShouldBe("Bệnh viện Đa khoa Thành phố Vinh");
    }

    [Fact]
    public async Task Department_update_is_versioned_in_history_and_audited()
    {
        using var client = await factory.LoginAsync("admin");
        var list = await client.GetFromJsonAsync<PagedResult<DepartmentDto>>("/api/admin/departments?search=KNHI", JsonDefaults.Options);
        var dept = list!.Items.Single(d => d.Code == "KNHI");

        using var update = await client.PutAsJsonAsync($"/api/admin/departments/{dept.Id}",
            new SaveDepartmentRequest(dept.Code, "Khoa Nhi (đổi tên)", dept.Type, dept.FacilityId, dept.HisDepartmentCode), JsonDefaults.Options);
        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        var history = await client.GetFromJsonAsync<List<HistoryEntryDto>>($"/api/admin/departments/{dept.Id}/history", JsonDefaults.Options);
        history!.Count.ShouldBeGreaterThanOrEqualTo(2);
        history[0].ChangeType.ShouldBe(HistoryChangeType.Updated);
        history[0].Snapshot.ShouldContain("Khoa Nhi (đổi tên)");
        history[0].ChangedByName.ShouldBe("Quản trị hệ thống");

        var audit = await client.GetFromJsonAsync<PagedResult<AuditLogDto>>(
            $"/api/admin/audit-logs?entityType=Department&entityId={dept.Id}&action=Update", JsonDefaults.Options);
        var entry = audit!.Items.First();
        entry.UserName.ShouldBe("admin");
        entry.OldValues.ShouldNotBeNull();
        JsonDocument.Parse(entry.OldValues!).RootElement.GetProperty("Name").GetString().ShouldNotBe("Khoa Nhi (đổi tên)");
    }

    [Fact]
    public async Task Audit_log_rejects_update_and_delete_at_database_level()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.AnyAsync()).ShouldBeTrue();

        var update = await Should.ThrowAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE audit_logs SET note = 'tampered'"));
        update.MessageText.ShouldContain("append-only");
        await Should.ThrowAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM audit_logs"));
    }

    [Fact]
    public async Task Audit_log_masks_password_hashes()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userCreates = await db.AuditLogs.Where(a => a.EntityType == "User" && a.Action == AuditActions.Create).Select(a => a.NewValues).Take(5).ToListAsync();

        userCreates.ShouldNotBeEmpty();
        userCreates.ShouldAllBe(v => JsonDocument.Parse(v!).RootElement.GetProperty("PasswordHash").GetString() == "***");
    }

    [Fact]
    public async Task Creating_supplier_with_account_provisions_supplier_admin()
    {
        using var client = await factory.LoginAsync("muasam");

        using var response = await client.PostAsJsonAsync("/api/admin/suppliers", new SaveSupplierRequest(
            "0109998887", "Công ty TNHH Thử Nghiệm", null, "Hà Nội", "Nguyễn Thử", "Giám đốc", null, null, null, "thu@example.vn", null,
            AdminAccount: new SupplierAccountRequest("ncc.thunghiem", "Nguyễn Thử", "thu@example.vn")), JsonDefaults.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var supplier = await response.Content.ReadFromJsonAsync<SupplierDto>(JsonDefaults.Options);
        supplier!.AccountCount.ShouldBe(1);

        using var duplicate = await client.PostAsJsonAsync("/api/admin/suppliers", new SaveSupplierRequest(
            "0109998887", "Trùng MST", null, null, null, null, null, null, null, null, null), JsonDefaults.Options);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invalid_tax_code_is_rejected_by_validation()
    {
        using var client = await factory.LoginAsync("muasam");

        using var response = await client.PostAsJsonAsync("/api/admin/suppliers", new SaveSupplierRequest(
            "12AB", "Sai MST", null, null, null, null, null, null, null, null, null), JsonDefaults.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("taxCode");
    }

    [Fact]
    public async Task Users_export_returns_xlsx()
    {
        using var client = await factory.LoginAsync("admin");

        using var response = await client.GetAsync(new Uri("/api/admin/users/export?userType=Internal", UriKind.Relative));
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        bytes[0].ShouldBe((byte)'P');
        bytes[1].ShouldBe((byte)'K');
    }

    [Fact]
    public async Task Department_list_filters_and_sorts_server_side()
    {
        using var client = await factory.LoginAsync("admin");

        var desc = await client.GetFromJsonAsync<PagedResult<DepartmentDto>>(
            "/api/admin/departments?type=Functional&sortBy=name&sortDesc=true&pageSize=50", JsonDefaults.Options);
        var asc = await client.GetFromJsonAsync<PagedResult<DepartmentDto>>(
            "/api/admin/departments?type=Functional&sortBy=name&pageSize=50", JsonDefaults.Options);
        var paged = await client.GetFromJsonAsync<PagedResult<DepartmentDto>>(
            "/api/admin/departments?type=Functional&sortBy=name&pageSize=2&page=2", JsonDefaults.Options);

        desc!.Items.ShouldAllBe(d => d.Type == DepartmentType.Functional);
        desc.Items.Select(d => d.Id).ShouldBe(asc!.Items.Select(d => d.Id).Reverse());
        paged!.Items.Select(d => d.Id).ShouldBe(asc.Items.Skip(2).Take(2).Select(d => d.Id));
        paged.Total.ShouldBe(asc.Total);
    }
}
