using Rihla.Models.Db;

namespace Rihla.Services.Db;

public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(int id);
    Task<AppUser?> GetByEmailAsync(string email);
    Task<AppUser?> GetByErpNextUserIdAsync(string erpNextUserId);
    Task<AppUser> UpsertAsync(
        string erpNextUserId, string email, string name,
        string? department, string? primaryRole, string? allowedModules,
        string? phone, string? avatarUrl);
    Task UpdateLastLoginAsync(int id);
    Task UpdateLastActivityAsync(int id);
    Task UpdatePasswordHashAsync(int id, string passwordHash);
    Task UpdateLanguageAsync(int id, string language);
}
