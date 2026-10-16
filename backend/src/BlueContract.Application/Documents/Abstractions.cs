using System.Text.Json.Nodes;

namespace BlueContract.Application.Documents;

/// <summary>DOCX template engine (spec §10): placeholders, filters, row/block loops, conditions, signature anchors.</summary>
public interface IDocumentGenerator
{
    /// <summary>Renders <paramref name="template"/> with <paramref name="data"/> and returns the resulting DOCX bytes.</summary>
    byte[] Render(byte[] template, JsonObject data);

    /// <summary>Lists every tag found in the template (e.g. "Contract.Number | money", "#Items", "/Items", "SIG_DIRECTOR").</summary>
    IReadOnlyList<string> ScanTags(byte[] template);
}

public enum RevisionKind
{
    Insertion = 0,
    Deletion = 1,
    Formatting = 2,
}

/// <summary>One tracked change (Track Changes) in a DOCX, as shown in the "thay đổi của NCC" list.</summary>
public sealed record DocxRevision(string Id, RevisionKind Kind, string? Author, DateTimeOffset? Date, string Text, string Context);

/// <summary>Reads and resolves tracked changes in DOCX files (supplier review loop, spec §6.1 B3).</summary>
public interface IDocxRevisions
{
    IReadOnlyList<DocxRevision> List(byte[] docx);

    /// <summary>Accepts (true) or rejects (false) the listed revisions; others stay tracked.</summary>
    byte[] Resolve(byte[] docx, IReadOnlyDictionary<string, bool> decisions);

    /// <summary>"Chốt bản": accepts every remaining tracked change.</summary>
    byte[] AcceptAll(byte[] docx);
}

/// <summary>DOCX → PDF conversion (Gotenberg/LibreOffice).</summary>
public interface IPdfConverter
{
    Task<byte[]> ConvertToPdfAsync(byte[] docx, string fileName, CancellationToken ct);
}

/// <summary>Browser-based DOCX editor with track changes (ONLYOFFICE Docs), replaceable behind this interface.</summary>
public interface IDocumentEditorService
{
    /// <summary>Public URL of the editor's JavaScript API, loaded by the browser.</summary>
    Uri EditorScriptBaseUrl { get; }

    /// <summary>Builds the signed editor configuration for a session.</summary>
    EditorConfig BuildConfig(EditorConfigRequest request);

    /// <summary>Validates and parses a save callback sent by the editor server.</summary>
    EditorCallback? ParseCallback(string body, string? authorizationHeader);

    /// <summary>Downloads the edited file from the URL given in the callback.</summary>
    Task<byte[]> DownloadEditedFileAsync(Uri url, CancellationToken ct);
}

public sealed record EditorConfigRequest(
    string SessionKey,
    string Title,
    Uri DownloadUrl,
    Uri CallbackUrl,
    string UserId,
    string UserName,
    EditorMode Mode);

public enum EditorMode
{
    /// <summary>Full editing (procurement, accountant accepting/rejecting changes).</summary>
    Edit = 0,

    /// <summary>Suggestions only: every change is a tracked revision (supplier review).</summary>
    Review = 1,

    /// <summary>Read only.</summary>
    View = 2,
}

/// <summary>Editor configuration object passed verbatim to the editor JS API (already contains the JWT token).</summary>
public sealed record EditorConfig(JsonObject Config);

public sealed record EditorCallback(string Key, int Status, Uri? Url, IReadOnlyList<string> Users)
{
    /// <summary>2 = ready for saving (all editors closed); 6 = force save while still editing.</summary>
    public bool HasNewFile => Status is 2 or 6 && Url is not null;

    /// <summary>2, 3 (save error) and 4 (closed without changes) end the session.</summary>
    public bool ClosesSession => Status is 2 or 3 or 4;
}
