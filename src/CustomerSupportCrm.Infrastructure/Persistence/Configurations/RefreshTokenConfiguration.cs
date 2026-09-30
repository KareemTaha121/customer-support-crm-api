using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();

        builder.Property(token => token.TokenHash).HasMaxLength(128);
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => token.SessionId);
        builder.HasIndex(token => token.UserId);

        builder.Property(token => token.RevokedReason).HasConversion<string>().HasMaxLength(32);
        builder.Property(token => token.CreatedByIp).HasMaxLength(RefreshToken.IpAddressMaxLength);
        builder.Property(token => token.UserAgent).HasMaxLength(RefreshToken.UserAgentMaxLength);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(token => token.IsSpent);

        // Two concurrent refreshes with the same token: only one rotation may win.
        builder.HasXminConcurrencyToken();
    }
}
