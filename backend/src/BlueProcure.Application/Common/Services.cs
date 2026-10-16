namespace BlueProcure.Application.Common;

/// <summary>Writes business-action audit entries (sign, return, login, download...) — data changes are audited automatically.</summary>
public interface IAuditLogger
{
    /// <summary>Adds the entry to the current unit of work (saved with the next SaveChanges).</summary>
    void Log(string action, string entityType, string? entityId, object? details = null, string? note = null);

    /// <summary>Writes the entry immediately in its own transaction (e.g. failed logins that roll back nothing else).</summary>
    Task LogNowAsync(string action, string entityType, string? entityId, object? details, string? note, CancellationToken ct);
}

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string HtmlBody, IReadOnlyList<string>? Cc = null);

/// <summary>Builds absolute links to the web app for emails (deep links).</summary>
public interface IAppLinkBuilder
{
    Uri Build(string relativePath);
}

/// <summary>Excel export for any list (spec §9.3).</summary>
public interface IExcelExporter
{
    byte[] Export<T>(string sheetTitle, IReadOnlyList<ExcelColumn<T>> columns, IEnumerable<T> rows);
}

public sealed record ExcelColumn<T>(string Header, Func<T, object?> Value, ExcelColumnFormat Format = ExcelColumnFormat.Text, double Width = 20);

public enum ExcelColumnFormat
{
    Text = 0,
    Number = 1,
    Money = 2,
    Date = 3,
    DateTime = 4,
}
