using CustomerSupportCrm.Domain.KnowledgeBase;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Infrastructure.Persistence;

public sealed partial class ApplicationDbContext
{
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();

    public DbSet<KnowledgeCategory> KnowledgeCategories => Set<KnowledgeCategory>();
}
