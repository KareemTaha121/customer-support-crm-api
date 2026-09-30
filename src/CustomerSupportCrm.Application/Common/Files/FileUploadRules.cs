using FluentValidation;
using FluentValidation.Results;

namespace CustomerSupportCrm.Application.Common.Files;

/// <param name="FileName">Sanitized original name, for display and download only.</param>
/// <param name="ContentType">Derived from the verified extension, never from the client.</param>
public sealed record ValidatedUpload(string FileName, string Extension, string ContentType, long Size);

/// <summary>
/// Upload validation: size, extension allow-list and file signature (magic bytes). The client's
/// declared MIME type is ignored. Malware scanning belongs in front of storage in production.
/// </summary>
public static class FileUploadRules
{
    public const long MaxAttachmentBytes = 20 * 1024 * 1024;
    public const long MaxImageBytes = 2 * 1024 * 1024;
    public const int FileNameMaxLength = 255;

    public const string FileEmpty = "FILE_EMPTY";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string FileTypeNotAllowed = "FILE_TYPE_NOT_ALLOWED";
    public const string FileSignatureMismatch = "FILE_SIGNATURE_MISMATCH";

    private static readonly byte[] Pdf = "%PDF"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Gif = "GIF8"u8.ToArray();
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] OleCompound = [0xD0, 0xCF, 0x11, 0xE0];

    /// <summary>Extension → (content type, accepted signatures). Null signature = text, checked for binary bytes.</summary>
    private static readonly Dictionary<string, (string ContentType, byte[][]? Signatures)> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("application/pdf", [Pdf]),
        [".png"] = ("image/png", [Png]),
        [".jpg"] = ("image/jpeg", [Jpeg]),
        [".jpeg"] = ("image/jpeg", [Jpeg]),
        [".gif"] = ("image/gif", [Gif]),
        [".webp"] = ("image/webp", [Riff]),
        [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [Zip]),
        [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [Zip]),
        [".pptx"] = ("application/vnd.openxmlformats-officedocument.presentationml.presentation", [Zip]),
        [".doc"] = ("application/msword", [OleCompound]),
        [".xls"] = ("application/vnd.ms-excel", [OleCompound]),
        [".zip"] = ("application/zip", [Zip]),
        [".txt"] = ("text/plain", null),
        [".csv"] = ("text/csv", null),
    };

    public static IReadOnlySet<string> Images { get; } = new HashSet<string>([".png", ".jpg", ".jpeg", ".gif", ".webp"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> Documents { get; } = new HashSet<string>(Types.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>Validates a seekable stream; its position is reset to 0 afterwards.</summary>
    public static async Task<ValidatedUpload> ValidateAsync(
        string fileName,
        Stream content,
        long length,
        long maxBytes,
        IReadOnlySet<string> allowedExtensions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(allowedExtensions);

        if (length <= 0)
        {
            throw Invalid(FileEmpty, "The file is empty.");
        }

        if (length > maxBytes)
        {
            throw Invalid(FileTooLarge, $"The file exceeds the {maxBytes / 1024 / 1024} MB limit.");
        }

        var safeName = Sanitize(fileName);
        var extension = Path.GetExtension(safeName);
        if (!allowedExtensions.Contains(extension) || !Types.TryGetValue(extension, out var type))
        {
            throw Invalid(FileTypeNotAllowed, "This file type is not allowed.");
        }

        var header = new byte[4096];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        content.Position = 0;
        var signatureOk = type.Signatures is null
            ? !header.AsSpan(0, read).Contains((byte)0)
            : type.Signatures.Any(signature => header.AsSpan(0, read).StartsWith(signature));
        if (!signatureOk)
        {
            throw Invalid(FileSignatureMismatch, "The file content does not match its extension.");
        }

#pragma warning disable CA1308 // File extensions are conventionally lower case.
        return new ValidatedUpload(safeName, extension.ToLowerInvariant(), type.ContentType, length);
#pragma warning restore CA1308
    }

    /// <summary>A storage key that never contains user input.</summary>
    public static string CreateStorageKey(string area, string extension, DateTimeOffset now) =>
        $"{area}/{now:yyyy}/{now:MM}/{Guid.CreateVersion7():N}{extension}";

    private static string Sanitize(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        name = new string([.. name.Where(c => !char.IsControl(c) && c is not '"' and not '\\' and not '/')]).Trim();
        if (name.Length == 0)
        {
            name = "file";
        }

        return name.Length <= FileNameMaxLength ? name : name[^FileNameMaxLength..];
    }

    private static ValidationException Invalid(string code, string message) =>
        new([new ValidationFailure("File", message) { ErrorCode = code }]);
}
