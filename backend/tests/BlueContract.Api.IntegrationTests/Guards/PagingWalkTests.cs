using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Guards;

/// <summary>
/// AUDIT-1 §12.3 / spec §9.3 "phân trang phía server": walking any list page by page gives exactly the rows of one big
/// page, in the same order — even when sorted on a column with many equal values (every ordering ends on the unique id).
/// </summary>
[Collection(ApiCollection.Name)]
public class PagingWalkTests(ApiFactory factory)
{
    private const int Small = 3;
    private const int Rows = 30;

    private static readonly string[] Lists =
    [
        "/api/acceptances", "/api/archive", "/api/admin/audit-logs", "/api/bid-decisions", "/api/call-orders", "/api/contracts",
        "/api/admin/departments", "/api/admin/facilities", "/api/admin/integration/logs", "/api/admin/integrity", "/api/items",
        "/api/liquidations", "/api/proposal-summaries", "/api/proposals", "/api/signing/inbox?tab=Sent", "/api/admin/suppliers",
        "/api/admin/users", "/api/admin/delegations", "/api/valuations", "/api/warehouse-receipts", "/api/admin/warehouses",
    ];

    /// <summary>Sorts with many ties (status, category, type…); unknown keys fall back to the list's default order.</summary>
    private static readonly string[] TiedSorts = ["status", "categoryName", "type", "isActive"];

    [Fact]
    public async Task Walking_every_list_page_by_page_never_repeats_or_skips_a_row()
    {
        var clients = new Dictionary<string, HttpClient>
        {
            ["admin"] = await factory.LoginAsync("admin"),
            ["muasam"] = await factory.LoginAsync("muasam"),
        };
        try
        {
            var checkedLists = 0;
            foreach (var list in Lists)
            {
                var client = await PickAsync(clients, list);
                foreach (var sort in TiedSorts.Prepend(string.Empty))
                {
                    var query = sort.Length == 0 ? string.Empty : $"sortBy={sort}&sortDesc=false";
                    var reference = await PageAsync(client, list, query, 1, Rows);
                    var walked = new List<string>();
                    for (var page = 1; walked.Count < reference.Ids.Count; page++)
                    {
                        var part = await PageAsync(client, list, query, page, Small);
                        part.Total.ShouldBe(reference.Total, $"{list} {query}: total changed between pages");
                        if (part.Ids.Count == 0)
                        {
                            break;
                        }

                        walked.AddRange(part.Ids);
                    }

                    walked.Take(reference.Ids.Count).ShouldBe(reference.Ids, $"{list} {query}: pages of {Small} differ from one page of {Rows}");
                    walked.Distinct().Count().ShouldBe(walked.Count, $"{list} {query}: a row appears on two pages");
                }

                checkedLists++;
            }

            checkedLists.ShouldBe(Lists.Length);
        }
        finally
        {
            foreach (var c in clients.Values)
            {
                c.Dispose();
            }
        }
    }

    private static async Task<HttpClient> PickAsync(Dictionary<string, HttpClient> clients, string list)
    {
        foreach (var client in clients.Values)
        {
            using var probe = await client.GetAsync(new Uri(Url(list, string.Empty, 1, 1), UriKind.Relative));
            if (probe.StatusCode == HttpStatusCode.OK)
            {
                return client;
            }
        }

        throw new InvalidOperationException($"no demo user may read {list}");
    }

    private static string Url(string list, string query, int page, int size)
    {
        var separator = list.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var extra = query.Length == 0 ? string.Empty : "&" + query;
        return $"{list}{separator}page={page}&pageSize={size}{extra}";
    }

    private static async Task<(int Total, List<string> Ids)> PageAsync(HttpClient client, string list, string query, int page, int size)
    {
        var json = await client.GetFromJsonAsync<JsonElement>(Url(list, query, page, size));
        var ids = json.GetProperty("items").EnumerateArray()
            .Select(i => i.TryGetProperty("id", out var id) ? id.ToString()
                : i.TryGetProperty("instanceId", out var wf) ? wf + "/" + (i.TryGetProperty("stepId", out var s) ? s.ToString() : string.Empty)
                : i.TryGetProperty("contractId", out var c) ? c.ToString()
                : i.GetRawText())
            .ToList();
        return (json.GetProperty("total").GetInt32(), ids);
    }
}
