using CustomerSupportCrm.Domain.KnowledgeBase;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Abstractions.Persistence;

public partial interface IApplicationDbContext
{
    DbSet<KnowledgeArticle> KnowledgeArticles { get; }

    DbSet<KnowledgeCategory> KnowledgeCategories { get; }
}
