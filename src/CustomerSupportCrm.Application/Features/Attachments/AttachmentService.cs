using CustomerSupportCrm.Application.Abstractions.Files;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Common.Files;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Domain.Attachments;
using CustomerSupportCrm.Domain.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Attachments;

public sealed record StoredFile(Stream Content, string ContentType, string FileName);

/// <summary>
/// Validates, stores and reads attachment files for any owner. Callers authorize access to the
/// owner first; this service only checks that the attachment belongs to it.
/// </summary>
public sealed class AttachmentService(IApplicationDbContext db, IFileStorage storage, TimeProvider time)
{
    public const string NotFoundCode = "ATTACHMENT_NOT_FOUND";

    /// <summary>Stores the file and adds the metadata row; the caller saves the unit of work.</summary>
    public async Task<Attachment> StoreAsync(
        string ownerType,
        Guid ownerId,
        Guid? parentId,
        string fileName,
        Stream content,
        long length,
        bool isPublic,
        UserId? uploadedBy,
        Guid? uploadedByCustomerId,
        CancellationToken cancellationToken)
    {
        var upload = await FileUploadRules.ValidateAsync(fileName, content, length, FileUploadRules.MaxAttachmentBytes, FileUploadRules.Documents, cancellationToken);
        var now = time.GetUtcNow();
        var key = FileUploadRules.CreateStorageKey(ownerType, upload.Extension, now);

        await storage.SaveAsync(key, content, upload.ContentType, cancellationToken);

        var attachment = Attachment.Create(ownerType, ownerId, parentId, upload.FileName, upload.ContentType, upload.Size, key, isPublic, uploadedBy, uploadedByCustomerId, now);
        db.Attachments.Add(attachment);
        return attachment;
    }

    public async Task<Attachment> GetAsync(Guid attachmentId, string ownerType, Guid ownerId, bool publicOnly, CancellationToken cancellationToken) =>
        await db.Attachments.SingleOrDefaultAsync(
            a => a.Id == attachmentId && a.OwnerType == ownerType && a.OwnerId == ownerId && (!publicOnly || a.IsPublic),
            cancellationToken)
        ?? throw new NotFoundException(NotFoundCode, "The attachment was not found.");

    public async Task<StoredFile> OpenAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        var stream = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken)
            ?? throw new NotFoundException(NotFoundCode, "The attachment file is missing.");
        return new StoredFile(stream, attachment.ContentType, attachment.FileName);
    }

    /// <summary>Removes the row, saves, then deletes the file.</summary>
    public async Task DeleteAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(attachment.StorageKey, cancellationToken);
    }

    public async Task<IReadOnlyList<AttachmentResponse>> ListAsync(string ownerType, Guid ownerId, bool publicOnly, Func<Guid, string> downloadUrl, CancellationToken cancellationToken)
    {
        var rows = await db.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId && (!publicOnly || a.IsPublic))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id,
                a.FileName,
                a.ContentType,
                a.Size,
                a.IsPublic,
                a.CreatedAt,
                UploadedByName = a.UploadedBy != null
                    ? db.Users.Where(u => u.Id == a.UploadedBy).Select(u => u.DisplayName).FirstOrDefault()
                    : db.Customers.IgnoreQueryFilters().Where(c => c.Id == a.UploadedByCustomerId).Select(c => c.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(a => new AttachmentResponse(a.Id, a.FileName, a.ContentType, a.Size, a.IsPublic, a.UploadedByName, a.CreatedAt, downloadUrl(a.Id)))];
    }

    /// <summary>Response for a just-stored attachment, with the uploader's name resolved the same way as <see cref="ListAsync"/>.</summary>
    public async Task<AttachmentResponse> ToResponseAsync(Attachment a, string downloadUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(a);
        var uploadedByName = a.UploadedBy is { } userId
            ? await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken)
            : a.UploadedByCustomerId is { } customerId
                ? await db.Customers.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == customerId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
                : null;
        return new(a.Id, a.FileName, a.ContentType, a.Size, a.IsPublic, uploadedByName, a.CreatedAt, downloadUrl);
    }

    /// <summary>Download result: attachment disposition and no content sniffing.</summary>
    public static IResult ToDownload(StoredFile file, HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(http);
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Stream(file.Content, file.ContentType, file.FileName);
    }
}
