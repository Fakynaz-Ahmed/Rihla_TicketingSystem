using System.Text.Json;

namespace Rihla.Services.ErpNext.Tools;

/// <summary>
/// Result returned from any ERPNext tool execution.
/// </summary>
public class ToolResult
{
    public bool Success { get; set; } = true;
    public string Data { get; set; } = string.Empty;
    public string? Error { get; set; }

    public static ToolResult Ok(object data) => new()
    {
        Success = true,
        Data    = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented       = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        })
    };

    public static ToolResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Executes named ERP tools on behalf of the AI.
/// Each tool maps directly to one or more ERPNext API calls.
/// </summary>
public interface IErpNextToolExecutor
{
    /// <summary>Execute a tool by name with the given JSON arguments string.</summary>
    Task<ToolResult> ExecuteAsync(string toolName, string argumentsJson, string userRole);

    /// <summary>Returns the OpenAI function definitions for all available tools.</summary>
    IReadOnlyList<object> GetToolDefinitions(string userRole);
}
