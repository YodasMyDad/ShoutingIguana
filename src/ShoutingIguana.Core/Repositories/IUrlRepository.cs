using System.Collections.Generic;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

public interface IUrlRepository
{
    Task<Url?> GetByIdAsync(int id);
    Task<Url?> GetByIdWithHeadersAsync(int id);
    Task<Url?> GetByAddressAsync(int projectId, string address);
    Task<Dictionary<string, Url>> GetByAddressesAsync(int projectId, IEnumerable<string> addresses);
    Task<IEnumerable<Url>> GetByProjectIdAsync(int projectId);
    Task<IEnumerable<Url>> GetByStatusAsync(int projectId, UrlStatus status);
    Task<List<Url>> GetCompletedUrlsAsync(int projectId);
    Task<List<int>> GetCompletedUrlIdsAsync(int projectId);
    Task<UrlAnalysisDto?> GetForAnalysisAsync(int id);
    Task<List<HeaderSnapshot>> GetHeadersAsync(int urlId);
    Task<string?> GetRenderedHtmlAsync(int id);
    Task<Url> CreateAsync(Url url);
    Task CreateBatchAsync(IEnumerable<Url> urls);
    Task<Url> UpdateAsync(Url url, IEnumerable<KeyValuePair<string, string>>? headers = null);
    Task<int> CountByProjectIdAsync(int projectId);
    Task<int> CountByStatusAsync(int projectId, UrlStatus status);
    Task<List<Url>> GetPagedByProjectIdAsync(int projectId, int skip, int take);
    Task<List<Url>> GetPagedByStatusesAsync(int projectId, IReadOnlyCollection<UrlStatus> statuses, int skip, int take);
    Task<int> CountByStatusesAsync(int projectId, IReadOnlyCollection<UrlStatus> statuses);
    Task<List<Url>> GetPagedErrorsAsync(int projectId, int skip, int take);
    Task<int> CountErrorsAsync(int projectId);
    Task DeleteByProjectIdAsync(int projectId);
}

