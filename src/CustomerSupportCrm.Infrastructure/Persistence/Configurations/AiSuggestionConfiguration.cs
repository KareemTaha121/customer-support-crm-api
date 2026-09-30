using CustomerSupportCrm.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class AiSuggestionConfiguration : IEntityTypeConfiguration<AiSuggestion>
{
    public void Configure(EntityTypeBuilder<AiSuggestion> builder)
    {
        builder.ToTable("ai_suggestions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Feature).HasMaxLength(30);
        builder.Property(s => s.Model).HasMaxLength(100);
        builder.Property(s => s.Output).HasColumnType("jsonb");
        builder.HasIndex(s => new { s.Feature, s.CreatedAt });
        builder.HasIndex(s => s.TicketId);
    }
}
