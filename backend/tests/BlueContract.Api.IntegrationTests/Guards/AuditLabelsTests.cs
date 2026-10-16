using System.Net.Http.Json;
using System.Text.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Guards;

/// <summary>
/// AUDIT-1 §12.5: the audit trail reads in Vietnamese — action and record names translated, the person's full name
/// instead of a login, no "Thao tác khác" / "Dữ liệu khác" fallbacks for anything the demo produced.
/// </summary>
[Collection(ApiCollection.Name)]
public class AuditLabelsTests(ApiFactory factory)
{
    [Fact]
    public async Task Audit_entries_carry_vietnamese_labels_and_full_names()
    {
        using var admin = await factory.LoginAsync("admin");
        var page = await admin.GetFromJsonAsync<JsonElement>("/api/admin/audit-logs?pageSize=200");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBeGreaterThan(50);
        foreach (var e in items)
        {
            var action = e.GetProperty("action").GetString();
            e.GetProperty("actionLabel").GetString().ShouldNotBe("Thao tác khác", $"action {action} has no Vietnamese name");
            e.GetProperty("entityLabel").GetString().ShouldNotBe("Dữ liệu khác", $"record kind {e.GetProperty("entityType").GetString()} has no Vietnamese name");
            if (e.GetProperty("userName").GetString() is { } login && login != "admin")
            {
                e.GetProperty("userFullName").GetString().ShouldNotBe(login, $"{login} should show as a full name");
            }
        }

        var kinds = await admin.GetFromJsonAsync<List<JsonElement>>("/api/admin/audit-logs/entity-types");
        kinds!.ShouldAllBe(k => k.GetProperty("label").GetString() != "Dữ liệu khác");
        var actions = await admin.GetFromJsonAsync<List<JsonElement>>("/api/admin/audit-logs/actions");
        actions!.ShouldContain(a => a.GetProperty("value").GetString() == "ContractCreate" && a.GetProperty("label").GetString() == "Tạo hợp đồng");
    }
}
