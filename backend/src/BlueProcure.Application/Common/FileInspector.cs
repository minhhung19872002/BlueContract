using System.IO.Compression;

namespace BlueProcure.Application.Common;

/// <summary>An uploaded file read into memory (uploads are size-limited at the API boundary).</summary>
public sealed record UploadedFile(string FileName, string ContentType, byte[] Content);

public enum FileKind
{
    Unknown = 0,
    Pdf = 1,
    Docx = 2,
    Xlsx = 3,
    Xml = 4,
    Jpeg = 5,
    Png = 6,
}

/// <summary>Identifies uploads by magic bytes, never by extension or declared content type (spec §11).</summary>
public static class FileInspector
{
    public const long MaxUploadBytes = 20 * 1024 * 1024;

    public static FileKind Detect(byte[] content)
    {
        if (content.Length < 4)
        {
            return FileKind.Unknown;
        }

        if (content[0] == '%' && content[1] == 'P' && content[2] == 'D' && content[3] == 'F')
        {
            return FileKind.Pdf;
        }

        if (content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return FileKind.Jpeg;
        }

        if (content[0] == 0x89 && content[1] == 'P' && content[2] == 'N' && content[3] == 'G')
        {
            return FileKind.Png;
        }

        if (content[0] == 'P' && content[1] == 'K' && content[2] == 3 && content[3] == 4)
        {
            return DetectOfficeZip(content);
        }

        return LooksLikeXml(content) ? FileKind.Xml : FileKind.Unknown;
    }

    private static FileKind DetectOfficeZip(byte[] content)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            if (zip.GetEntry("word/document.xml") is not null)
            {
                return FileKind.Docx;
            }

            if (zip.GetEntry("xl/workbook.xml") is not null)
            {
                return FileKind.Xlsx;
            }
        }
        catch (InvalidDataException)
        {
            return FileKind.Unknown;
        }

        return FileKind.Unknown;
    }

    private static bool LooksLikeXml(byte[] content)
    {
        var start = 0;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            start = 3; // UTF-8 BOM
        }

        while (start < content.Length && char.IsWhiteSpace((char)content[start]))
        {
            start++;
        }

        return start < content.Length && content[start] == '<';
    }
}

/// <summary>Short-lived signed tokens for URLs called by external services (ONLYOFFICE downloads/callbacks).</summary>
public interface IUrlSigner
{
    string Sign(string value, TimeSpan lifetime);

    bool TryValidate(string? token, out string value);
}
