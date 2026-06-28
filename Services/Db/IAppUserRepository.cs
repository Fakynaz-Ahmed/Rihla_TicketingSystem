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
    /// <summary>Returns all users whose Department matches any of the given department names.</summary>
    Task<List<AppUser>> GetByDepartmentsAsync(List<string> departments);
    /// <summary>Returns all users whose PrimaryRole matches the given role string (e.g. AppRoles.CustomerCare).</summary>
    Task<List<AppUser>> GetByPrimaryRoleAsync(string primaryRole);
}

