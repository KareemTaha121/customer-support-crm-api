namespace CustomerSupportCrm.Contracts.Common;

/// <param name="DownloadUrl">Relative API path; requires the same authorization as the owner.</param>
public sealed record AttachmentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long Size,
    bool IsPublic,
    string? UploadedByName,
    DateTimeOffset CreatedAt,
    string DownloadUrl);
