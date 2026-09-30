using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

/// <summary>Stores strongly typed ids as uuid columns.</summary>
internal static class StronglyTypedIdConverters
{
    internal sealed class UserIdConverter() : ValueConverter<UserId, Guid>(id => id.Value, value => new UserId(value));

    internal sealed class RoleIdConverter() : ValueConverter<RoleId, Guid>(id => id.Value, value => new RoleId(value));
}
