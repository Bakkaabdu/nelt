namespace Nelt.Application.Common;

/// <summary>Framework-neutral representation of an uploaded file.</summary>
public sealed record FileUpload(Stream Content, string FileName, long Length)
{
    public string Extension => Path.GetExtension(FileName).ToLowerInvariant();

    /// <summary>File name safe to store and send back in Content-Disposition.</summary>
    public string SafeFileName
    {
        get
        {
            var name = Path.GetFileName(FileName);
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Length > 180 ? name[^180..] : name;
        }
    }
}

public enum FileCategory
{
    Image = 1,
    Video = 2,
    Audio = 3,
    Document = 4,
    Material = 5,
    Submission = 6,
}

/// <summary>Allow-lists and size limits for every upload category.</summary>
public static class FilePolicy
{
    private static readonly string[] Images = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] Videos = [".mp4", ".webm", ".m4v"];
    private static readonly string[] AudioFiles = [".mp3", ".m4a", ".wav", ".ogg", ".aac"];
    private static readonly string[] Documents = [".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".txt", ".epub", ".odt"];

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
        [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".webm"] = "video/webm",
        [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".wav"] = "audio/wav", [".ogg"] = "audio/ogg", [".aac"] = "audio/aac",
        [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".epub"] = "application/epub+zip",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".odt"] = "application/vnd.oasis.opendocument.text",
        [".zip"] = "application/zip",
    };

    public static IReadOnlyList<string> AllowedExtensions(FileCategory category) => category switch
    {
        FileCategory.Image => Images,
        FileCategory.Video => Videos,
        FileCategory.Audio => AudioFiles,
        FileCategory.Document => Documents,
        FileCategory.Material => [.. Documents, .. AudioFiles, .. Images],
        FileCategory.Submission => [.. Documents, .. AudioFiles, .. Images, ".zip"],
        _ => [],
    };

    public static long MaxBytes(FileCategory category) => category switch
    {
        FileCategory.Image => 5L * 1024 * 1024,
        FileCategory.Video => 2L * 1024 * 1024 * 1024,
        FileCategory.Audio => 200L * 1024 * 1024,
        FileCategory.Submission => 50L * 1024 * 1024,
        _ => 200L * 1024 * 1024,
    };

    public static string ContentTypeFor(string fileNameOrExtension)
    {
        var ext = fileNameOrExtension.StartsWith('.') ? fileNameOrExtension : Path.GetExtension(fileNameOrExtension);
        return ContentTypes.TryGetValue(ext, out var type) ? type : "application/octet-stream";
    }

    public static bool IsAudio(string fileName) => AudioFiles.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    /// <summary>Formats that browsers can display inline safely (never HTML/SVG).</summary>
    public static bool IsInlineSafe(string contentType)
        => contentType.StartsWith("image/", StringComparison.Ordinal)
           || contentType.StartsWith("video/", StringComparison.Ordinal)
           || contentType.StartsWith("audio/", StringComparison.Ordinal)
           || contentType == "application/pdf";

    public static Error? Validate(FileUpload file, FileCategory category)
    {
        if (file.Length <= 0)
        {
            return Error.Validation("The uploaded file is empty.");
        }

        if (!AllowedExtensions(category).Contains(file.Extension))
        {
            return Error.Validation("This file type is not allowed.");
        }

        return file.Length > MaxBytes(category) ? Error.Validation("The file is too large.") : null;
    }
}
