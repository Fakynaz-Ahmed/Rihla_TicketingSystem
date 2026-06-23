using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rihla.Config;
using Rihla.DTOs;
using Rihla.Services.Cache;
using Rihla.Services.ErpNext.Tools;

namespace Rihla.Services.Ai;

/// <summary>
/// OpenAI GPT-4o service with ERPNext Tool Calling.
///
/// Flow per message:
///   1. Build messages list (system + history + user message)
///   2. Call GPT-4o with tool definitions
///   3. If GPT wants to call a tool → execute via ErpNextToolExecutor → feed result back
///   4. Repeat until GPT returns a text response (max 5 tool rounds)
///   5. Return final assistant text
/// </summary>
public class AiService : IAiService
{
    private readonly HttpClient _http;
    private readonly OpenAiSettings _settings;
    private readonly IErpNextToolExecutor _tools;
    private readonly ILogger<AiService> _logger;
    private readonly ICacheService _cache;

    /// <summary>Redis key where the overridden AI API key is stored.</summary>
    public const string CacheKey = "settings:ai-api-key";

    //private const string OpenAiChatUrl = "https://api.openai.com/v1/chat/completions";
    private const string OpenAiChatUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
    private const int MaxToolRounds = 5;

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AiService(
        HttpClient http,
        IOptions<OpenAiSettings> settings,
        IErpNextToolExecutor tools,
        ICacheService cache,
        ILogger<AiService> logger)
    {
        _http     = http;
        _settings = settings.Value;
        _tools    = tools;
        _cache    = cache;
        _logger   = logger;
    }

    /// <summary>
    /// Returns the effective API key: Redis override first, then appsettings fallback.
    /// </summary>
    private async Task<string> GetApiKeyAsync()
    {
        var overrideKey = await _cache.GetAsync(CacheKey);
        return !string.IsNullOrWhiteSpace(overrideKey) ? overrideKey : _settings.ApiKey;
    }

    public async Task<string> ChatAsync(
        string conversationId,
        string userMessage,
        IReadOnlyList<(string Role, string Content)> history,
        string userRole,
        string preferredLanguage = "ar",
        byte[]? imageBytes = null,
        string? mimeType = null)
    {
        // Build the language hint
        var langHint = preferredLanguage == "ar"
            ? "Always respond in Arabic unless the user writes in English."
            : "Respond in English.";

        // Build messages list
        var messages = new List<object>
        {
            new { role = "system", content = $"{_settings.SystemPrompt}\n\n{langHint}" }
        };

        // Add conversation history (last 20 messages to stay within context)
        foreach (var (role, content) in history.TakeLast(20))
        {
            messages.Add(new { role = role == "ai" ? "assistant" : "user", content });
        }

        // Add current user message
        if (imageBytes != null && mimeType != null)
        {
            var base64 = Convert.ToBase64String(imageBytes);
            var dataUrl = $"data:{mimeType};base64,{base64}";
            messages.Add(new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "image_url", image_url = new { url = dataUrl } },
                    new { type = "text", text = string.IsNullOrWhiteSpace(userMessage) ? "Analyze this image." : userMessage }
                }
            });
        }
        else
        {
            messages.Add(new { role = "user", content = userMessage });
        }

        // Get tool definitions filtered by role
        var toolDefs = _tools.GetToolDefinitions(userRole);

        // Tool-calling loop
        for (var round = 0; round < MaxToolRounds; round++)
        {
            var requestBody = new
            {
                model      = _settings.Model,
                messages,
                tools      = toolDefs,
                tool_choice = "auto",
                max_tokens = _settings.MaxTokens,
                temperature = _settings.Temperature
            };

            var responseJson = await CallOpenAiAsync(requestBody);
            if (responseJson is null)
                return "عذراً، حدث خطأ في الاتصال بالمساعد الذكي. يرجى المحاولة مرة أخرى.";

            var root    = responseJson.Value;
            var choices = root.GetProperty("choices");
            var choice  = choices[0];
            var message = choice.GetProperty("message");

            // Check finish reason
            var finishReason = choice.GetProperty("finish_reason").GetString();

            // Extract assistant message content (may be null when tool_calls present)
            string? assistantContent = null;
            if (message.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
                assistantContent = contentEl.GetString();

            // If no tool calls → return the text response
            if (finishReason == "stop" || !message.TryGetProperty("tool_calls", out var toolCallsEl))
            {
                return assistantContent ?? string.Empty;
            }

            // Add assistant message (with tool_calls) to conversation
            messages.Add(BuildAssistantMessage(message));

            // Execute each tool call
            foreach (var toolCall in toolCallsEl.EnumerateArray())
            {
                var callId   = toolCall.GetProperty("id").GetString()!;
                var function = toolCall.GetProperty("function");
                var name     = function.GetProperty("name").GetString()!;
                var args     = function.TryGetProperty("arguments", out var argsEl)
                               ? argsEl.GetString() ?? "{}"
                               : "{}";

                _logger.LogInformation("[Conv {Id}] Tool call [{Round}]: {Name}", conversationId, round + 1, name);

                var result = await _tools.ExecuteAsync(name, args, userRole);

                var toolContent = result.Success
                    ? result.Data
                    : $"{{\"error\": \"{result.Error}\"}}";

                messages.Add(new
                {
                    role         = "tool",
                    tool_call_id = callId,
                    content      = toolContent
                });
            }

            // Loop again with tool results
        }

        _logger.LogWarning("[Conv {Id}] Max tool rounds reached without final answer.", conversationId);
        return "تعذّر الحصول على إجابة واضحة. يرجى إعادة صياغة السؤال.";
    }

    public async Task<PassportExtractionResult?> ExtractPassportDataAsync(byte[] imageBytes, string mimeType)
    {
        var base64Image = Convert.ToBase64String(imageBytes);
        var imageUrl = $"data:{mimeType};base64,{base64Image}";

        var requestBody = new
        {
            model = _settings.Model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = imageUrl } },
                        new { type = "text", text = @"You are a specialized bilingual OCR model for Egyptian passports. 
Analyze the passport image and extract ALL visible fields in both Arabic and English exactly as printed.

VALIDATION RULES:
- If this is NOT a passport at all: set 'is_passport' to false, 'is_clear' to false, all other fields null.
- If it IS a passport but it shows a COVER PAGE, BLANK PAGE, VISA PAGE, or any page other than the MAIN BIO DATA PAGE (the page with the person's photo, full name, passport number): set 'is_passport' to true, 'is_clear' to false, provide an Arabic error_message asking the user to upload the main bio data page.
- If the image is TOO BLURRY or UNREADABLE: set 'is_passport' to true, 'is_clear' to false, provide an Arabic error_message.
- If the main bio data page is clearly visible: set 'is_passport' to true, 'is_clear' to true, and extract all fields.

EXTRACTION RULES:
- Extract EVERY field visible in BOTH Arabic and English sides of the page.
- For Egyptian passports, 'place_of_birth' must be a known Egyptian governorate in English (correct OCR errors, e.g. 'FAYOUR' → 'FAYOUM', 'ALKAHERA' → 'CAIRO').
- 'national_id' is the Egyptian national ID number printed on the page (الرقم القومي), usually 14 digits.
- 'profession' is the job/occupation in English (e.g. 'CIVIL ENGINEER').
- 'profession_ar' is the job/occupation in Arabic (e.g. 'مهندس مدني').
- 'address' is the full address printed on the page in Arabic or English.
- 'military_status' must be exactly one of these Arabic strings: 'غير مطلوب للتجنيد', 'في سن التجنيد', or 'معافى مؤقت'. If it is a female's passport or not printed, set it to null.
- 'date_of_issue' is the passport issue date (تاريخ الإصدار) in YYYY-MM-DD format.
- 'gender_ar' should be 'ذكر' for Male or 'أنثى' for Female.
- For any field not visible or not present on this passport, use null.

Return ONLY a valid JSON object with NO markdown formatting, NO ```json``` blocks. Just the raw JSON.

JSON Schema:
{
  ""is_passport"": true/false,
  ""is_clear"": true/false,
  ""passport_number"": ""string or null"",
  ""full_name"": ""ENGLISH full name exactly as printed or null"",
  ""full_name_ar"": ""الاسم الكامل بالعربية كما هو مطبوع أو null"",
  ""nationality"": ""English nationality or null"",
  ""nationality_ar"": ""الجنسية بالعربية أو null"",
  ""date_of_birth"": ""YYYY-MM-DD or null"",
  ""expiry_date"": ""YYYY-MM-DD or null"",
  ""date_of_issue"": ""YYYY-MM-DD or null"",
  ""issuing_country"": ""string or null"",
  ""gender"": ""M or F or null"",
  ""gender_ar"": ""ذكر أو أنثى أو null"",
  ""place_of_birth"": ""English governorate name or null"",
  ""place_of_birth_ar"": ""مكان الميلاد بالعربية أو null"",
  ""profession"": ""English profession or null"",
  ""profession_ar"": ""المهنة بالعربية أو null"",
  ""national_id"": ""14-digit national ID number as string or null"",
  ""address"": ""full address string or null"",
  ""military_status"": ""military status string or null"",
  ""error_message"": ""Arabic error message if not clear or wrong page, otherwise null""
}" }
                    }
                }
            },
            temperature = 0.1,
            max_tokens = 800
        };

        var responseJson = await CallOpenAiAsync(requestBody);
        if (responseJson is null) return null;

        try
        {
            var choices = responseJson.Value.GetProperty("choices");
            var choice = choices[0];
            var message = choice.GetProperty("message");
            string? assistantContent = null;
            if (message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                assistantContent = contentEl.GetString();

            if (string.IsNullOrWhiteSpace(assistantContent)) return null;

            var jsonString = assistantContent.Trim();
            if (jsonString.StartsWith("```json")) jsonString = jsonString[7..];
            if (jsonString.StartsWith("```")) jsonString = jsonString[3..];
            if (jsonString.EndsWith("```")) jsonString = jsonString[..^3];
            jsonString = jsonString.Trim();

            var result = JsonSerializer.Deserialize<PassportExtractionResult>(jsonString, _jsonOpts);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing passport extraction response from Gemini");
            return null;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<JsonElement?> CallOpenAiAsync(object body)
    {
        var apiKey  = await GetApiKeyAsync();
        var json    = JsonSerializer.Serialize(body, _jsonOpts);
        using var req = new HttpRequestMessage(HttpMethod.Post, OpenAiChatUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var resp     = await _http.SendAsync(req);
            var respBody = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI returned {Status}: {Body}", (int)resp.StatusCode, respBody);
                return null;
            }

            return JsonDocument.Parse(respBody).RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI HTTP call threw");
            return null;
        }
    }

    /// <summary>
    /// Reconstructs the assistant message object from the GPT response
    /// in a format that can be re-serialized back to OpenAI.
    /// </summary>
    private static object BuildAssistantMessage(JsonElement message)
    {
        // We need to pass it back verbatim, so serialize → deserialize as object
        return JsonSerializer.Deserialize<object>(message.GetRawText(), _jsonOpts)!;
    }
}
