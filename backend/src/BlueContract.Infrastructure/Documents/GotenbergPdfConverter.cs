using System.Net.Http.Headers;
using BlueContract.Application.Documents;
using Microsoft.Extensions.Logging;

namespace BlueContract.Infrastructure.Documents;

/// <summary>DOCX → PDF through Gotenberg's LibreOffice route (POST /forms/libreoffice/convert).</summary>
internal sealed class GotenbergPdfConverter(IHttpClientFactory httpClientFactory, ILogger<GotenbergPdfConverter> logger) : IPdfConverter
{
    public async Task<byte[]> ConvertToPdfAsync(byte[] docx, string fileName, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient(DependencyInjection.GotenbergClient);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(docx);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        var safeName = Path.GetFileNameWithoutExtension(fileName);
        form.Add(file, "files", (string.IsNullOrWhiteSpace(safeName) ? "document" : safeName) + ".docx");

        using var response = await client.PostAsync(new Uri("/forms/libreoffice/convert", UriKind.Relative), form, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Gotenberg conversion failed: {Status} {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"PDF conversion failed with HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
