namespace Rihla.Services.Cache;

public interface ICacheService
{
    Task SetAsync(string key, string value, TimeSpan expiration);
    Task<string?> GetAsync(string key);
    Task RemoveAsync(string key);
    Task RemoveByPrefixAsync(string prefix);
    Task<bool> ExistsAsync(string key);
    Task<long> IncrementAsync(string key);
    Task<int>  GetIntAsync(string key);
    Task       ResetIntAsync(string key);
}
