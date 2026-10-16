namespace BlueProcure.Infrastructure;

public sealed class ExternalServicesOptions
{
    public const string SectionName = "ExternalServices";

    /// <summary>Gotenberg base URL (DOCX → PDF).</summary>
    public Uri GotenbergUrl { get; set; } = new("http://localhost:3000");
}
