using Rihla.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rihla.Services.Ai;

public interface IAiService
{
    /// <summary>
    /// Send a message in a conversation and get a response.
    /// Internally handles tool calling loop with ERPNext.
    /// Supports optional multimodal image.
    /// </summary>
    Task<string> ChatAsync(
        string conversationId,
        string userMessage,
        IReadOnlyList<(string Role, string Content)> history,
        string userRole,
        string preferredLanguage = "ar",
        byte[]? imageBytes = null,
        string? mimeType = null);

    /// <summary>
    /// Checks if the image is a passport and extracts its data.
    /// </summary>
    Task<PassportExtractionResult?> ExtractPassportDataAsync(byte[] imageBytes, string mimeType);
}
