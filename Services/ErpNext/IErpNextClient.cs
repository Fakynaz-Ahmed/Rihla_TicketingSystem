namespace Rihla.Services.ErpNext;

/// <summary>Represents an ERPNext user fetched via the REST API.</summary>
public class ErpNextUser
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public string? Department { get; set; }
    public string? Phone { get; set; }
    public string? UserImage { get; set; }
}

/// <summary>HTTP client wrapper for ERPNext REST API v2.</summary>
public interface IErpNextClient
{
    /// <summary>
    /// Verify user credentials against ERPNext.
    /// Returns the user's ERPNext username on success, null on failure.
    /// </summary>
    Task<ErpNextUser?> AuthenticateAsync(string email, string password);

    /// <summary>Fetch user info + roles using admin API key.</summary>
    Task<ErpNextUser?> GetUserAsync(string email);


    Task<List<Dictionary<string, object?>>> GetDocListViaMethodAsync(
    string doctype,
    Dictionary<string, string>? filters = null,
    List<string>? fields = null,
    int limit = 50);
    /// <summary>
    /// Execute a generic list query on any DocType.
    /// Returns list of objects as JSON strings.
    /// </summary>
    Task<List<Dictionary<string, object?>>> GetDocListAsync(
        string doctype,
        Dictionary<string, string>? filters = null,
        List<string>? fields = null,
        int limit = 50,
        string? orderBy = null);

    /// <summary>Fetch a single document by name.</summary>
    Task<Dictionary<string, object?>?> GetDocAsync(string doctype, string name);

    /// <summary>Run a named ERPNext Script Report and return rows.</summary>
    Task<List<Dictionary<string, object?>>> RunReportAsync(
        string reportName,
        Dictionary<string, string>? filters = null);

    /// <summary>Execute a whitelisted server-side method.</summary>
    Task<string?> CallMethodAsync(string method, Dictionary<string, string>? args = null);
}
