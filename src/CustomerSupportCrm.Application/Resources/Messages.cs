namespace CustomerSupportCrm.Application.Resources;

/// <summary>
/// Marker type for <c>IStringLocalizer&lt;Messages&gt;</c>. Keys are stable error/message codes;
/// values live in Messages.resx (English, neutral) and Messages.ar.resx.
/// A code thrown with several or parameterized English messages (INVALID_TICKET, FILE_TOO_LARGE, ...)
/// is only in Messages.ar.resx, so English keeps the specific message from the throw site.
/// </summary>
public sealed class Messages;
