using Microsoft.AspNetCore.Http;

namespace Rihla.Services.Storage;

/// <summary>
/// Abstraction for user avatar storage.
/// Swap LocalStorageService for an S3/Azure/GCS implementation without touching callers.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Saves a base64-encoded image as the user's avatar.
    /// Returns the public relative URL (e.g. /avatars/5.jpg).
    /// </summary>
    Task<string> SaveAvatarAsync(int odooUserId, string base64Data);

    /// <summary>
    /// Generates and saves a default SVG avatar using the user's initials.
    /// Returns the public relative URL (e.g. /avatars/5.svg).
    /// </summary>
    Task<string> CreateDefaultAvatarAsync(int odooUserId, string name);

    /// <summary>
    /// Saves a base64-encoded file attachment (image, audio, video, document).
    /// Returns the public URL.
    /// </summary>
    Task<string> SaveAttachmentAsync(string base64Data, string mimeType, string conversationId);

    /// <summary>
    /// Saves an uploaded IFormFile attachment directly (no base64 overhead).
    /// Returns the public URL.
    /// </summary>
    Task<string> SaveAttachmentFromFileAsync(IFormFile file, string conversationId);

    /// <summary>
    /// Saves a base64-encoded thumbnail image for a product.template machine.
    /// Returns the public absolute URL, or empty string on failure.
    /// </summary>
    Task<string> SaveMachineImageAsync(int machineOdooId, string base64Data);

    /// <summary>
    /// Saves a .docx document for a product.template machine.
    /// Returns the public absolute URL, or empty string on failure.
    /// </summary>
    Task<string> SaveMachineDocumentAsync(int machineOdooId, byte[] data);

    /// <summary>
    /// Saves the closing attachment uploaded by an engineer for a visit.
    /// Returns the public absolute URL.
    /// </summary>
    Task<string> SaveVisitAttachmentAsync(int visitId, IFormFile file);

    /// <summary>
    /// Re-bases a stored URL so it always uses the configured AppBaseUrl.
    /// Handles URLs that were saved with an old IP or hostname.
    /// Returns null if the input is null or empty.
    /// </summary>
    string? NormalizeUrl(string? url);
}
