using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Rihla.Services.Storage;

/// <summary>
/// Stores avatars on the local filesystem under wwwroot/avatars/.
/// To migrate to S3/Azure/GCS: implement IStorageService and swap the DI registration.
/// </summary>
public class LocalStorageService : IStorageService
{
    private readonly string _avatarDir;
    private readonly string _attachmentDir;
    private readonly string _machineImageDir;
    private readonly string _machineDocDir;
    private readonly string _visitAttachmentDir;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<LocalStorageService> _logger;
    private readonly string? _appBaseUrl;

    private static readonly string[] Colors =
    [
        "#4F46E5", "#0EA5E9", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6", "#EC4899", "#14B8A6"
    ];

    public LocalStorageService(
        IWebHostEnvironment env,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        ILogger<LocalStorageService> logger)
    {
        // WebRootPath is null when wwwroot doesn't exist yet — fall back to creating it
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _avatarDir = Path.Combine(webRoot, "avatars");
        _attachmentDir = Path.Combine(webRoot, "attachments");
        _machineImageDir = Path.Combine(webRoot, "machine-images");
        _machineDocDir   = Path.Combine(webRoot, "machine-docs");
        _visitAttachmentDir = Path.Combine(webRoot, "visit-attachments");
        
        Directory.CreateDirectory(_avatarDir);
        Directory.CreateDirectory(_attachmentDir);
        Directory.CreateDirectory(_machineImageDir);
        Directory.CreateDirectory(_machineDocDir);
        Directory.CreateDirectory(_visitAttachmentDir);
        
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _appBaseUrl = configuration["AppBaseUrl"]?.TrimEnd('/');
    }

    public async Task<string> SaveAvatarAsync(int odooUserId, string base64Data)
    {
        try
        {
            // Strip data URI prefix if present (data:image/jpeg;base64,...)
            var comma = base64Data.IndexOf(',');
            var raw = comma >= 0 ? base64Data[(comma + 1)..] : base64Data;

            var bytes = Convert.FromBase64String(raw);
            var path = Path.Combine(_avatarDir, $"{odooUserId}.jpg");
            await File.WriteAllBytesAsync(path, bytes);
            _logger.LogInformation("Saved avatar for user {UserId}", odooUserId);
            return BuildFullUrl($"/avatars/{odooUserId}.jpg");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save avatar for user {UserId}, falling back to default", odooUserId);
            return await CreateDefaultAvatarAsync(odooUserId, odooUserId.ToString());
        }
    }

    public async Task<string> CreateDefaultAvatarAsync(int odooUserId, string name)
    {
        var initials = BuildInitials(name);
        var color = Colors[odooUserId % Colors.Length];
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="200" height="200" viewBox="0 0 200 200">
              <rect width="200" height="200" fill="{color}" rx="100"/>
              <text x="100" y="100" dominant-baseline="central" text-anchor="middle"
                    font-family="Arial,sans-serif" font-size="80" font-weight="bold" fill="white">{initials}</text>
            </svg>
            """;

        var path = Path.Combine(_avatarDir, $"{odooUserId}.svg");
        await File.WriteAllTextAsync(path, svg);
        _logger.LogInformation("Created default avatar for user {UserId}", odooUserId);
        return BuildFullUrl($"/avatars/{odooUserId}.svg");
    }

    public async Task<string> SaveAttachmentFromFileAsync(IFormFile file, string conversationId)
    {
        var ext = MimeToExtension(file.ContentType);
        var fileName = $"{conversationId}_{Guid.NewGuid():N}.{ext}";
        var path = Path.Combine(_attachmentDir, fileName);
        await using var stream = File.Create(path);
        await file.CopyToAsync(stream);
        _logger.LogInformation("Saved attachment {FileName} for conversation {ConvId}", fileName, conversationId);
        return BuildFullUrl($"/attachments/{fileName}");
    }

    public async Task<string> SaveAttachmentAsync(string base64Data, string mimeType, string conversationId)
    {
        var comma = base64Data.IndexOf(',');
        var raw = comma >= 0 ? base64Data[(comma + 1)..] : base64Data;
        var bytes = Convert.FromBase64String(raw);

        var ext = MimeToExtension(mimeType);
        var fileName = $"{conversationId}_{Guid.NewGuid():N}.{ext}";
        var path = Path.Combine(_attachmentDir, fileName);
        await File.WriteAllBytesAsync(path, bytes);
        _logger.LogInformation("Saved attachment {FileName} for conversation {ConvId}", fileName, conversationId);
        return BuildFullUrl($"/attachments/{fileName}");
    }

    public async Task<string> SaveMachineImageAsync(int machineOdooId, string base64Data)
    {
        try
        {
            var comma = base64Data.IndexOf(',');
            var raw = comma >= 0 ? base64Data[(comma + 1)..] : base64Data;
            var bytes = Convert.FromBase64String(raw);
            var path = Path.Combine(_machineImageDir, $"{machineOdooId}.jpg");
            await File.WriteAllBytesAsync(path, bytes);
            _logger.LogInformation("Saved image for machine {MachineOdooId}", machineOdooId);
            return BuildFullUrl($"/machine-images/{machineOdooId}.jpg");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save image for machine {MachineOdooId}", machineOdooId);
            return string.Empty;
        }
    }

    public async Task<string> SaveMachineDocumentAsync(int machineOdooId, byte[] data)
    {
        try
        {
            var path = Path.Combine(_machineDocDir, $"{machineOdooId}.docx");
            await File.WriteAllBytesAsync(path, data);
            _logger.LogInformation("Saved document for machine {MachineOdooId}", machineOdooId);
            return BuildFullUrl($"/machine-docs/{machineOdooId}.docx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save document for machine {MachineOdooId}", machineOdooId);
            return string.Empty;
        }
    }

    public async Task<string> SaveVisitAttachmentAsync(int visitId, IFormFile file)
    {
        var ext = MimeToExtension(file.ContentType);
        var fileName = $"visit_{visitId}_{Guid.NewGuid():N}.{ext}";
        var path = Path.Combine(_visitAttachmentDir, fileName);
        await using var stream = File.Create(path);
        await file.CopyToAsync(stream);
        _logger.LogInformation("Saved close attachment {FileName} for visit {VisitId}", fileName, visitId);
        return BuildFullUrl($"/visit-attachments/{fileName}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        // If already using the correct base, return as-is
        if (!string.IsNullOrEmpty(_appBaseUrl) && url.StartsWith(_appBaseUrl))
            return url;

        // Extract path+query from whatever host was stored, then rebuild
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return BuildFullUrl(uri.PathAndQuery);

        // Already a relative path — just build it
        return BuildFullUrl(url);
    }

    private string BuildFullUrl(string relativePath)
    {
        if (!string.IsNullOrEmpty(_appBaseUrl))
            return $"{_appBaseUrl}{relativePath}";

        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null) return relativePath;

        var request = ctx.Request;
        return $"{request.Scheme}://{request.Host}{relativePath}";
    }

    private static string MimeToExtension(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => "jpg",
        "image/png"  => "png",
        "image/gif"  => "gif",
        "image/webp" => "webp",
        "audio/mpeg" => "mp3",
        "audio/ogg"  => "ogg",
        "audio/wav"  => "wav",
        "video/mp4"  => "mp4",
        "application/pdf" => "pdf",
        _ => "bin"
    };

    private static string BuildInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return char.ToUpper(parts[0][0]).ToString();
        return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[^1][0])}";
    }
}
