using CustomerSupportCrm.Domain.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class KnowledgeArticleConfiguration : IEntityTypeConfiguration<KnowledgeArticle>
{
    public void Configure(EntityTypeBuilder<KnowledgeArticle> builder)
    {
        builder.ToTable("knowledge_articles");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Title).HasMaxLength(KnowledgeArticle.TitleMaxLength);
        builder.Property(a => a.Slug).HasMaxLength(KnowledgeArticle.SlugMaxLength);
        builder.HasIndex(a => a.Slug).IsUnique();
        builder.Property(a => a.Summary).HasMaxLength(KnowledgeArticle.SummaryMaxLength);
        builder.Property(a => a.Body).HasMaxLength(KnowledgeArticle.BodyMaxLength);
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Visibility).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Language).HasMaxLength(5);
        builder.Property(a => a.Tags).HasColumnType("text[]");
        builder.Ignore(a => a.IsVisibleToCustomers);
        builder.HasIndex(a => new { a.Status, a.Visibility, a.Language });
        builder.HasIndex(a => a.CategoryId);
        builder.HasIndex(a => a.TranslationOfId);
        builder.HasOne<KnowledgeCategory>().WithMany().HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasXminConcurrencyToken();
    }
}

internal sealed class KnowledgeCategoryConfiguration : IEntityTypeConfiguration<KnowledgeCategory>
{
    public void Configure(EntityTypeBuilder<KnowledgeCategory> builder)
    {
        builder.ToTable("knowledge_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(KnowledgeCategory.NameMaxLength);
        builder.Property(c => c.NameAr).HasMaxLength(KnowledgeCategory.NameMaxLength);
        builder.Property(c => c.Description).HasMaxLength(1000);
        builder.HasOne<KnowledgeCategory>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}
