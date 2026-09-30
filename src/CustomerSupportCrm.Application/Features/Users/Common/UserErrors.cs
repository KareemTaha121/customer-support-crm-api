namespace CustomerSupportCrm.Application.Features.Users.Common;

public static class UserErrors
{
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string EmailTaken = "EMAIL_TAKEN";
    public const string UnknownRole = "UNKNOWN_ROLE";
    public const string CannotDisableSelf = "CANNOT_DISABLE_SELF";
    public const string LastAdministrator = "LAST_ADMINISTRATOR";
}
