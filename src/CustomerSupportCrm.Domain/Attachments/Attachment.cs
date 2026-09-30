using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Attachments;

public static class AttachmentOwnerTypes
{
    public const string Customer = "customer";
    public const string Ticket = "ticket";
    public const string KnowledgeArticle = "kb_article";
}

/// <summary>
/// Metadata of a stored file. The binary lives in object storage under <see cref="StorageKey"/>;
/// the owner (customer, ticket, article) is referenced by type and id.
/// </summary>
public sealed class Attachment : Entity<Guid>
{
    public const int FileNameMaxLength = 255;

    private Attachment()
    {
        OwnerType = string.Empty;
        FileName = string.Empty;
        ContentType = string.Empty;
        StorageKey = string.Empty;
    }

    private Attachment(Guid id)
        : base(id)
    {
        OwnerType = string.Empty;
        FileName = string.Empty;
        ContentType = string.Empty;
        StorageKey = string.Empty;
    }

    public string OwnerType { get; private set; }

    public Guid OwnerId { get; private set; }

    /// <summary>Optional finer owner, e.g. the ticket message a file was sent with.</summary>
    public Guid? ParentId { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long Size { get; private set; }

    public string StorageKey { get; private set; }

    /// <summary>Visible to the customer (portal/email) when true; internal otherwise.</summary>
    public bool IsPublic { get; private set; }

    public UserId? UploadedBy { get; private set; }

    public Guid? UploadedByCustomerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Attachment Create(
        string ownerType,
        Guid ownerId,
        Guid? parentId,
        string fileName,
        string contentType,
        long size,
        string storageKey,
        bool isPublic,
        UserId? uploadedBy,
        Guid? uploadedByCustomerId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        return new Attachment(Guid.CreateVersion7())
        {
            OwnerType = ownerType,
            OwnerId = ownerId,
            ParentId = parentId,
            FileName = fileName,
            ContentType = contentType,
            Size = size,
            StorageKey = storageKey,
            IsPublic = isPublic,
            UploadedBy = uploadedBy,
            UploadedByCustomerId = uploadedByCustomerId,
            CreatedAt = now,
        };
    }

    /// <summary>Links a pre-uploaded file to the message it was sent with.</summary>
    public void AttachTo(Guid parentId, bool isPublic)
    {
        ParentId = parentId;
        IsPublic = isPublic;
    }
}
