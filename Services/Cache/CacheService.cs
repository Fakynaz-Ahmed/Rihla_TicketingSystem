using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Rihla.Config;

namespace Rihla.Services.Cache;

public class CacheService : ICacheService
{
    private readonly IDatabase _db;
    private readonly string _instanceName;
    private readonly ILogger<CacheService> _logger;

    public CacheService(IConnectionMultiplexer redis, IOptions<RedisSettings> settings, ILogger<CacheService> logger)
    {
        _db = redis.GetDatabase();
        _instanceName = settings.Value.InstanceName;
        _logger = logger;
    }

    public async Task SetAsync(string key, string value, TimeSpan expiration)
    {
        try { await _db.StringSetAsync(Prefix(key), value, expiration); }
        catch (Exception ex) { _logger.LogError(ex, "Redis SET failed for key: {Key}", key); throw; }
    }

    public async Task<string?> GetAsync(string key)
    {
        try
        {
            var value = await _db.StringGetAsync(Prefix(key));
            return value.IsNullOrEmpty ? null : value.ToString();
        }
        catch (Exception ex) { _logger.LogError(ex, "Redis GET failed for key: {Key}", key); return null; }
    }

    public async Task RemoveAsync(string key)
    {
        try { await _db.KeyDeleteAsync(Prefix(key)); }
        catch (Exception ex) { _logger.LogError(ex, "Redis DEL failed for key: {Key}", key); throw; }
    }

    public async Task RemoveByPrefixAsync(string prefix)
    {
        try
        {
            var server  = _db.Multiplexer.GetServer(_db.Multiplexer.GetEndPoints()[0]);
            var pattern = $"{_instanceName}{prefix}*";
            var keys    = server.Keys(pattern: pattern).ToArray();
            if (keys.Length > 0)
                await _db.KeyDeleteAsync(keys);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Redis pattern DEL failed for prefix: {Prefix}", prefix); }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try { return await _db.KeyExistsAsync(Prefix(key)); }
        catch (Exception ex) { _logger.LogError(ex, "Redis EXISTS failed for key: {Key}", key); return false; }
    }

    public async Task<long> IncrementAsync(string key)
    {
        try   { return await _db.StringIncrementAsync(Prefix(key)); }
        catch (Exception ex) { _logger.LogError(ex, "Redis INCR failed for key: {Key}", key); return 0; }
    }

    public async Task<int> GetIntAsync(string key)
    {
        try
        {
            var v = await _db.StringGetAsync(Prefix(key));
            return v.IsNullOrEmpty ? 0 : (int)v;
        }
        catch (Exception ex) { _logger.LogError(ex, "Redis GET(int) failed for key: {Key}", key); return 0; }
    }

    public async Task ResetIntAsync(string key)
    {
        try   { await _db.KeyDeleteAsync(Prefix(key)); }
        catch (Exception ex) { _logger.LogError(ex, "Redis DEL failed for key: {Key}", key); }
    }

    private string Prefix(string key) => $"{_instanceName}{key}";
}
