using Microsoft.EntityFrameworkCore;
using Rihla.Data;
using Rihla.Models.Db;

namespace Rihla.Services.Db;

public class AppUserRepository : IAppUserRepository
{
    private readonly AppDbContext _db;

    public AppUserRepository(AppDbContext db) => _db = db;

    public Task<AppUser?> GetByIdAsync(int id) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id);

    public Task<AppUser?> GetByEmailAsync(string email) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower());

    public Task<AppUser?> GetByErpNextUserIdAsync(string erpNextUserId) =>
        _db.Users.FirstOrDefaultAsync(u => u.ErpNextUserId == erpNextUserId);

    public async Task<AppUser> UpsertAsync(
        string erpNextUserId, string email, string name,
        string? department, string? primaryRole, string? allowedModules,
        string? phone, string? avatarUrl)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.ErpNextUserId == erpNextUserId);

        if (user is null)
        {
            user = new AppUser
            {
                ErpNextUserId  = erpNextUserId,
                Email          = email.ToLower(),
                Name           = name,
                Department     = department,
                PrimaryRole    = primaryRole,
                AllowedModules = allowedModules,
                Phone          = phone,
                AvatarUrl      = avatarUrl,
                CreatedAt      = DateTime.UtcNow,
                LastLoginAt    = DateTime.UtcNow
            };
            _db.Users.Add(user);
        }
        else
        {
            user.Email          = email.ToLower();
            user.Name           = name;
            user.Department     = department;
            user.PrimaryRole    = primaryRole;
            user.AllowedModules = allowedModules;
            user.Phone          = phone ?? user.Phone;
            user.AvatarUrl      = avatarUrl ?? user.AvatarUrl;
            user.LastLoginAt    = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return user;
    }

    public async Task UpdateLastLoginAsync(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return;
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateLastActivityAsync(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return;
        user.LastLoginAt = DateTime.UtcNow; // Or separate LastActivityAt if we want, but LastLoginAt is perfectly fine.
        await _db.SaveChangesAsync();
    }

    public async Task UpdatePasswordHashAsync(int id, string passwordHash)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return;
        user.PasswordHash = passwordHash;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateLanguageAsync(int id, string language)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return;
        user.Language = language;
        await _db.SaveChangesAsync();
    }

    public async Task<List<AppUser>> GetByDepartmentsAsync(List<string> departments)
    {
        return await _db.Users
            .Where(u => u.Department != null && departments.Contains(u.Department))
            .ToListAsync();
    }

    public async Task<List<AppUser>> GetByPrimaryRoleAsync(string primaryRole)
    {
        return await _db.Users
            .Where(u => u.PrimaryRole != null && u.PrimaryRole == primaryRole)
            .ToListAsync();
    }
}

