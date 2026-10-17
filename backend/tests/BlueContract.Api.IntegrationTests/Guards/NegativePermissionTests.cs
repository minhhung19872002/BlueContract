using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using BlueContract.Api.Auth;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Guards;

/// <summary>
/// AUDIT-1 §13.8 "phân quyền âm": a read-only account (Viewer: master data, contracts, archive, reports) is refused —
/// 403 before anything runs — by every endpoint that changes data and needs a permission it lacks. The list comes from
/// the running API, so a new endpoint is covered without editing this test.
/// </summary>
[Collection(ApiCollection.Name)]
public partial class NegativePermissionTests(ApiFactory factory)
{
    [GeneratedRegex(@"\{(?<name>[^}:]+)(:(?<constraint>[^}]+))?\}")]
    private static partial Regex RouteParameter();

    [Fact]
    public async Task A_read_only_account_cannot_change_anything()
    {
        var viewer = RoleCatalog.All.Single(r => r.Code == RoleCatalog.Viewer).Permissions.ToHashSet(StringComparer.Ordinal);
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(e => (Endpoint: e, Methods: e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [], Permission: e.Metadata.OfType<HasPermissionAttribute>().LastOrDefault()))
            .Where(x => x.Permission is not null && !x.Permission.Permissions.Any(viewer.Contains))
            .SelectMany(x => x.Methods.Where(m => m is "POST" or "PUT" or "PATCH" or "DELETE")
                .Select(m => (Method: m, Pattern: x.Endpoint.RoutePattern.RawText!, Upload: IsUpload(x.Endpoint))))
            .Distinct()
            .ToList();
        endpoints.Count.ShouldBeGreaterThan(80, "the sweep must cover the write endpoints of the API");

        using var client = await factory.LoginAsync("xem");
        var allowed = new List<string>();
        foreach (var (method, pattern, upload) in endpoints)
        {
            var url = "/" + RouteParameter().Replace(pattern.TrimStart('/'), m => m.Groups["constraint"].Value switch
            {
                "guid" => Guid.Empty.ToString(),
                "int" or "long" => "1",
                _ => "x",
            });
            // The body matches what the endpoint consumes (multipart for uploads), so only the permission can refuse it.
            HttpContent? body = method == "DELETE" ? null : upload ? Multipart() : JsonContent.Create(new { });
            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(url, UriKind.Relative)) { Content = body };
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                allowed.Add($"{method} {url} → {(int)response.StatusCode}");
            }
        }

        allowed.ShouldBeEmpty("a read-only account reached these write endpoints: " + string.Join("; ", allowed));
    }

    private static bool IsUpload(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()?.MethodInfo.GetParameters()
            .Any(p => p.ParameterType == typeof(IFormFile)) == true;

    private static MultipartFormDataContent Multipart()
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("%PDF-1.4"u8.ToArray()), "file", "a.pdf");
        return form;
    }
}
