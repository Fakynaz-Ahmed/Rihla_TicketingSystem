namespace Rihla.Services.Db;

public interface IDeviceTokenRepository
{
    Task UpsertAsync(int userId, string token, string platform);
    Task RemoveAsync(int userId, string token);
    Task<List<string>> GetByUserIdAsync(int userId);
    Task<List<string>> GetByUserIdsAsync(IEnumerable<int> userIds);
}
