using System.Text.Json.Serialization;

namespace Rihla.DTOs;

public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message) =>
        new() { Success = false, Message = message };
}

public class PagedResult<T>
{
    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
}

public class ApiErrorResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = false;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("errors")]
    public Dictionary<string, string>? Errors { get; set; }
}
