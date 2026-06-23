using System.Text.Json.Serialization;

namespace Rihla.DTOs;

public class SyncResultDto
{
    [JsonPropertyName("total_users_fetched")]
    public int TotalUsersFetched { get; set; }

    [JsonPropertyName("users_created")]
    public int UsersCreated { get; set; }

    [JsonPropertyName("users_updated")]
    public int UsersUpdated { get; set; }

    [JsonPropertyName("failed_users")]
    public List<string> FailedUsers { get; set; } = [];

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
