namespace CustomerSupportCrm.Application.Abstractions.Files;

/// <summary>
/// Binary object storage for attachments and logos. The database keeps metadata only.
/// Keys are generated server-side; callers never pass user input as a key.
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>Returns null when the object does not exist.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
