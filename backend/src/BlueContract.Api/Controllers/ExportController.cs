using BlueContract.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueContract.Api.Controllers;

/// <summary>"Xuất Excel" of any table of the web app, laid out from the rows the page displays (spec §9.3).</summary>
[Route("api/export")]
[Authorize]
public sealed class ExportController(TableExportService exporter) : ApiControllerBase
{
    /// <summary>Workbook of the given columns and rows; reads no data (the export shows only what the user sees).</summary>
    [HttpPost("table")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public IActionResult Table(TableExportRequest request)
    {
        var result = exporter.Export(request);
        return result.IsSuccess ? Excel(result.Value.Content, result.Value.FileName) : Problem(result.Error!);
    }
}
