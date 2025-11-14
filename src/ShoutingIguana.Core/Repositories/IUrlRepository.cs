using System.Collections.Generic;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

public interface IUrlRepository
{
    Task<Url?> GetByIdAsync(int id);
    Task<Url?> GetByIdWithHeadersAsync(int id);
    Task<Url?> GetByAddressAsync(int projectId, string address);
    Task<IEnumerable<Url>> GetByProjectIdAsync(int projectId);
    Task<IEnumerable<Url>> GetByStatusAsync(int projectId, UrlStatus status);
    Task<List<Url>> GetCompletedUrlsAsync(int projectId);
    Task<List<int>> GetCompletedUrlIdsAsync(int projectId);
    Task<UrlAnalysisDto?> GetForAnalysisAsync(int id);
    Task<List<HeaderSnapshot>> GetHeadersAsync(int urlId);
    Task<string?> GetRenderedHtmlAsync(int id);
    Task<Url> CreateAsync(Url url);
    Task<Url> UpdateAsync(Url url, IEnumerable<KeyValuePair<string, string>>? headers = null);
    Task<int> CountByProjectIdAsync(int projectId);
    Task<int> CountByStatusAsync(int projectId, UrlStatus status);
    Task DeleteByProjectIdAsync(int projectId);
}

