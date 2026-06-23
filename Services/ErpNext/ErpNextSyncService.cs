using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rihla.Data;
using Rihla.DTOs;
using Rihla.Services.Auth;
using Rihla.Services.Db;

namespace Rihla.Services.ErpNext;

public class ErpNextSyncService : IErpNextSyncService
{
    private readonly IErpNextClient _erpClient;
    private readonly IAppUserRepository _userRepo;
    private readonly AppDbContext _db;
    private readonly ILogger<ErpNextSyncService> _logger;

    public ErpNextSyncService(
        IErpNextClient erpClient,
        IAppUserRepository userRepo,
        AppDbContext db,
        ILogger<ErpNextSyncService> logger)
    {
        _erpClient = erpClient;
        _userRepo = userRepo;
        _db = db;
        _logger = logger;
    }

    public async Task<SyncResultDto> SyncUsersAsync()
    {
        var result = new SyncResultDto();
        try
        {
            _logger.LogInformation("Starting ERPNext users synchronization...");

            // 1. Fetch Users (Enabled only)
            var usersRaw = await _erpClient.GetDocListAsync(
                doctype: "User",
                filters: new Dictionary<string, string> { ["enabled"] = "1" },
                fields: new List<string> { "name", "email", "full_name", "phone", "user_image" },
                limit: 2000
            );

            if (usersRaw == null || usersRaw.Count == 0)
            {
                _logger.LogWarning("No enabled users found in ERPNext.");
                result.Success = true;
                result.Message = "No users found to synchronize.";
                return result;
            }

            result.TotalUsersFetched = usersRaw.Count;

            //// 2. Fetch User Roles in Bulk
            //var rolesRaw = await _erpClient.GetDocListViaMethodAsync(
            //    doctype: "Has Role",
            //    filters: new Dictionary<string, string> { ["parenttype"] = "User" },
            //    fields: new List<string> { "parent", "role" },
            //    limit: 15000
            //);

            //var rolesGrouped = rolesRaw?
            //    .GroupBy(r => r.TryGetValue("parent", out var p) ? p?.ToString() : null)
            //    .Where(g => !string.IsNullOrEmpty(g.Key))
            //    .ToDictionary(
            //        g => g.Key!,
            //        g => g.Select(r => r.TryGetValue("role", out var rl) ? rl?.ToString() : null)
            //              .Where(r => !string.IsNullOrEmpty(r))
            //              .Cast<string>()
            //              .ToList()
            //    ) ?? [];


            //// 2. Fetch User Roles per user (Frappe doesn't allow bulk queries on child tables)
            //var rolesGrouped = new Dictionary<string, List<string>>();

            //foreach (var rawUser in usersRaw)
            //{
            //    string? userName = rawUser.TryGetValue("name", out var nVal) ? nVal?.ToString() : null;
            //    if (string.IsNullOrEmpty(userName)) continue;

            //    var roleRows = await _erpClient.GetDocListAsync(
            //        doctype: "Has Role",
            //        filters: new Dictionary<string, string> { ["parent"] = userName, ["parenttype"] = "User" },
            //        fields: new List<string> { "parent", "role" },
            //        limit: 100
            //    );

            //    var roles = roleRows
            //        .Select(r => r.TryGetValue("role", out var rl) ? rl?.ToString() : null)
            //        .Where(r => !string.IsNullOrEmpty(r))
            //        .Cast<string>()
            //        .ToList();

            //    rolesGrouped[userName] = roles;
            //}

            // 2. Fetch User Roles via User document (roles child table comes embedded)
            var rolesGrouped = new Dictionary<string, List<string>>();

            foreach (var rawUser in usersRaw)
            {
                string? userName = rawUser.TryGetValue("name", out var nVal) ? nVal?.ToString() : null;
                if (string.IsNullOrEmpty(userName)) continue;

                var userDoc = await _erpClient.GetDocAsync("User", userName);
                if (userDoc is null)
                {
                    rolesGrouped[userName] = [];
                    continue;
                }

                var roles = new List<string>();
                if (userDoc.TryGetValue("roles", out var rolesObj) && rolesObj is List<object?> rolesList)
                {
                    foreach (var item in rolesList)
                    {
                        if (item is Dictionary<string, object?> roleDict &&
                            roleDict.TryGetValue("role", out var roleVal) &&
                            roleVal is string roleStr &&
                            !string.IsNullOrEmpty(roleStr))
                        {
                            roles.Add(roleStr);
                        }
                    }
                }

                rolesGrouped[userName] = roles;
            }
            // 3. Fetch Departments from Employee DocType
            var employeesRaw = await _erpClient.GetDocListAsync(
                doctype: "Employee",
                filters: new Dictionary<string, string> { ["status"] = "Active" },
                fields: new List<string> { "user_id", "department" },
                limit: 2000
            );

            var departmentMap = employeesRaw?
                .GroupBy(e => e.TryGetValue("user_id", out var uid) ? uid?.ToString() : null)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(
                    g => g.Key!,
                    g => g.First().TryGetValue("department", out var dept) ? dept?.ToString() : null
                ) ?? [];

            // 4. Map and Sync Users
            int createdCount = 0;
            int updatedCount = 0;

            // Fetch existing users from local DB to verify if user exists (to count created vs updated)
            var existingUsers = await _db.Users
                .Select(u => new { u.ErpNextUserId, u.Id })
                .ToDictionaryAsync(u => u.ErpNextUserId, u => u.Id);

            foreach (var rawUser in usersRaw)
            {
                string? name = rawUser.TryGetValue("name", out var n) ? n?.ToString() : null;
                string? email = rawUser.TryGetValue("email", out var e) ? e?.ToString() : null;
                string? fullName = rawUser.TryGetValue("full_name", out var fn) ? fn?.ToString() : null;
                string? phone = rawUser.TryGetValue("phone", out var ph) ? ph?.ToString() : null;
                string? userImage = rawUser.TryGetValue("user_image", out var img) ? img?.ToString() : null;

                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // If email is missing, fall back to username/name if it looks like an email, or ignore
                if (string.IsNullOrEmpty(email))
                {
                    email = name.Contains("@") ? name : $"{name}@company.local";
                }

                try
                {
                    // Resolve roles
                    rolesGrouped.TryGetValue(name, out var roles);
                    roles ??= [];

                    var primaryRole = AuthService.DeterminePrimaryRole(roles);
                    var allowedModules = AuthService.DetermineModules(roles);
                    var modulesJson = JsonSerializer.Serialize(allowedModules);

                    // Resolve department
                    departmentMap.TryGetValue(name, out var department);

                    bool isNew = !existingUsers.ContainsKey(name);

                    await _userRepo.UpsertAsync(
                        erpNextUserId: name,
                        email: email,
                        name: string.IsNullOrWhiteSpace(fullName) ? name : fullName,
                        department: department,
                        primaryRole: primaryRole,
                        allowedModules: modulesJson,
                        phone: phone,
                        avatarUrl: userImage
                    );

                    if (isNew)
                        createdCount++;
                    else
                        updatedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync ERPNext user: {Name}", name);
                    result.FailedUsers.Add($"{name} (Error: {ex.Message})");
                }
            }

            result.UsersCreated = createdCount;
            result.UsersUpdated = updatedCount;
            result.Success = true;
            result.Message = $"Successfully synced. Created: {createdCount}, Updated: {updatedCount}, Failed: {result.FailedUsers.Count}.";

            _logger.LogInformation("ERPNext users sync finished. Created={Created}, Updated={Updated}, Failed={Failed}",
                createdCount, updatedCount, result.FailedUsers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during ERPNext users sync");
            result.Success = false;
            result.Message = $"Sync failed due to an error: {ex.Message}";
        }

        return result;
    }
}
