using System.Text.Json;

namespace Rihla.Services.Cache;

public static class CacheExtensions
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Returns the cached value if present; otherwise calls factory, stores the result, and returns it.
    /// Deserialization errors are swallowed so a corrupted cache entry falls through to the factory.
    /// </summary>
    public static async Task<T> GetOrSetAsync<T>(
        this ICacheService cache, string key, TimeSpan ttl, Func<Task<T>> factory)
        where T : notnull
    {
        var cached = await cache.GetAsync(key);
        if (cached is not null)
        {
            try { return JsonSerializer.Deserialize<T>(cached, _opts)!; }
            catch { /* fall through */ }
        }

        var value = await factory();
        try { await cache.SetAsync(key, JsonSerializer.Serialize(value, _opts), ttl); }
        catch { /* non-fatal: serve uncached */ }

        return value;
    }
}
