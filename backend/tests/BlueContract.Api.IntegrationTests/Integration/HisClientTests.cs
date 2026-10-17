using System.Net;
using System.Text;
using BlueContract.Application.Integration;
using BlueContract.Infrastructure.Integration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Integration;

public class HisClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://his.example.vn/") };
    }

    private static RestHisClient Client(StubHandler handler, HisOptions options) =>
        new(new Factory(handler), Options.Create(options), NullLogger<RestHisClient>.Instance);

    [Fact]
    public async Task Rest_client_maps_vendor_fields_from_configuration()
    {
        const string body = """
            { "ketQua": { "danhSach": [
              { "maKey": "K001", "maHang": "TH001", "tenHang": "Paracetamol 500mg", "dvt": "Viên", "soLuong": 20000, "donGia": "450",
                "hang": { "ten": "DHG" } }
            ] } }
            """;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        var options = new HisOptions
        {
            Provider = "Rest",
            ItemsPath = "/hang-trung-thau?soQd={bidNumber}&mst={taxCode}",
            ItemsArrayPath = "ketQua.danhSach",
            ItemFields = new Dictionary<string, string>
            {
                ["HisKey"] = "maKey",
                ["Code"] = "maHang",
                ["Name"] = "tenHang",
                ["Unit"] = "dvt",
                ["Quantity"] = "soLuong",
                ["UnitPrice"] = "donGia",
                ["Manufacturer"] = "hang.ten",
            },
        };

        var items = await Client(handler, options).GetItemsByBidDecisionAsync("125/QĐ-BV", "0101234567", null, CancellationToken.None);

        handler.LastUri!.AbsoluteUri.ShouldBe("https://his.example.vn/hang-trung-thau?soQd=125%2FQ%C4%90-BV&mst=0101234567");
        var item = items.ShouldHaveSingleItem();
        item.HisKey.ShouldBe("K001");
        item.Name.ShouldBe("Paracetamol 500mg");
        item.Quantity.ShouldBe(20_000);
        item.UnitPrice.ShouldBe(450);
        item.Manufacturer.ShouldBe("DHG");
    }

    [Fact]
    public async Task Rest_client_reports_errors_as_his_unavailable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        await Should.ThrowAsync<HisUnavailableException>(() =>
            Client(handler, new HisOptions()).GetStockMovementAsync("KT01", ["K001"], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), CancellationToken.None));
    }

    [Fact]
    public async Task Mock_client_is_deterministic_and_balances_stock()
    {
        var mock = new MockHisClient(Options.Create(new HisOptions()), new BlueContract.Application.Integration.MockHisReceiptQueue());
        var first = await mock.GetItemsByBidDecisionAsync("125/QĐ-BV", "0101234567", null, CancellationToken.None);
        var second = await mock.GetItemsByBidDecisionAsync("125/QĐ-BV", "0101234567", null, CancellationToken.None);
        second.ShouldBe(first);
        first.Count.ShouldBeInRange(8, 15);

        var stock = await mock.GetStockMovementAsync("KT01", [.. first.Select(i => i.HisKey)], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), CancellationToken.None);
        stock.ShouldAllBe(s => s.Closing == s.Opening + s.Received - s.Issued && s.Closing >= 0);
    }

    private sealed class Clock : BlueContract.Application.Common.IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 17, 1, 0, 0, TimeSpan.Zero);

        public DateOnly TodayLocal => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    /// <summary>A HIS that only knows bearer tokens (His:AuthMode=Login), like his.bluestar.com.vn.</summary>
    private sealed class JwtHis : HttpMessageHandler
    {
        public int Logins { get; private set; }

        public string Current { get; set; } = "t1";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/login")
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (!body.Contains("\"svc-bluecontract\"", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }

                Logins++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{ "success": true, "data": { "token": "{{Current}}" } }""", Encoding.UTF8, "application/json"),
                };
            }

            return request.Headers.Authorization?.Parameter == Current
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "success": true, "data": [ { "hisKey": "TH001", "closing": 7 } ] }""", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
    }

    private static (RestHisClient Client, Clock Clock) LoginClient(JwtHis his)
    {
        var options = Options.Create(new HisOptions { Provider = "Rest", BaseUrl = "https://his.example.vn", AuthMode = "Login", Username = "svc-bluecontract", Password = "p" });
        var clock = new Clock();
        var handler = new HisLoginHandler(new HisTokenCache(clock), options, NullLogger<HisLoginHandler>.Instance) { InnerHandler = his };
        return (new RestHisClient(new Factory(handler), options, NullLogger<RestHisClient>.Instance), clock);
    }

    [Fact]
    public async Task Login_mode_reuses_one_token_and_logs_in_again_when_it_expires()
    {
        var his = new JwtHis();
        var (client, clock) = LoginClient(his);

        (await client.GetStockMovementAsync("KT01", ["TH001"], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 17), CancellationToken.None))
            .ShouldHaveSingleItem().Closing.ShouldBe(7);
        await client.GetStockMovementAsync("KT01", ["TH001"], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 17), CancellationToken.None);
        his.Logins.ShouldBe(1);

        clock.UtcNow = clock.UtcNow.AddMinutes(26);
        await client.GetStockMovementAsync("KT01", ["TH001"], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 17), CancellationToken.None);
        his.Logins.ShouldBe(2);
    }

    [Fact]
    public async Task Login_mode_logs_in_again_once_when_the_token_is_revoked()
    {
        var his = new JwtHis();
        var (client, _) = LoginClient(his);
        await client.GetStockMovementAsync("KT01", ["TH001"], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 17), CancellationToken.None);

        his.Current = "t2";   // HIS restarted / password rotated: the cached token is now refused
        (await client.GetStockMovementAsync("KT01", ["TH001"], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 17), CancellationToken.None))
            .ShouldHaveSingleItem();
        his.Logins.ShouldBe(2);
    }
}
