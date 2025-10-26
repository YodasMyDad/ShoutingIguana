using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

public interface IHreflangRepository
{
    Task<Hreflang> CreateAsync(Hreflang hreflang);
    Task<List<Hreflang>> CreateBatchAsync(List<Hreflang> hreflangs);
    Task<List<Hreflang>> GetByUrlIdAsync(int urlId);
    Task<List<Hreflang>> GetByLanguageCodeAsync(int projectId, string languageCode);
    Task DeleteByUrlIdAsync(int urlId);
}

