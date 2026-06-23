using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rihla.Config;

namespace Rihla.Services.ErpNext;

public class ErpNextClient : IErpNextClient
{
    private readonly HttpClient _http;
    private readonly ErpNextSettings _settings;
    private readonly ILogger<ErpNextClient> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public ErpNextClient(
        HttpClient http,
        IOptions<ErpNextSettings> settings,
        ILogger<ErpNextClient> logger)
    {
        _http     = http;
        _settings = settings.Value;
        _logger   = logger;

        // Set base address
        _http.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
    }

    // ── Auth helpers ──────────────────────────────────────────────────────────

    /// <summary>Adds the admin API Key + Secret as Authorization header.</summary>
    private void AddAdminAuth(HttpRequestMessage req)
    {
        var authValue = $"Token {_settings.ApiKey}:{_settings.ApiSecret}";
        req.Headers.TryAddWithoutValidation("Authorization", authValue);
        
        // Log masked token details to verify it is being attached correctly
        var maskedSecret = _settings.ApiSecret.Length > 4 
            ? _settings.ApiSecret[..4] + "..." 
            : "...";
        _logger.LogDebug("Attaching Auth Header: Token {ApiKey}:{MaskedSecret}", _settings.ApiKey, maskedSecret);
    }

    // ── IErpNextClient ────────────────────────────────────────────────────────

    public async Task<ErpNextUser?> AuthenticateAsync(string email, string password)
    {
        // ERPNext login endpoint returns a session cookie + user info
        var payload = new { usr = email, pwd = password };
        var json    = JsonSerializer.Serialize(payload);

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/method/login")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        try
        {
            var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("ERPNext login failed for {Email}: {Status} {Body}", email, (int)resp.StatusCode, body);
                return null;
            }

            // On success, fetch full user info via admin token
            return await GetUserAsync(email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERPNext AuthenticateAsync threw for {Email}", email);
            return null;
        }
    }

    public async Task<ErpNextUser?> GetUserAsync(string email)
    {
        // Fetch user document
        var userDoc = await GetDocAsync("User", email);
        if (userDoc is null) return null;

        // 1. Try to fetch roles from the embedded child table in the user document (Preferred & secure)
        var roles = new List<string>();
        if (userDoc.TryGetValue("roles", out var rolesObj) && rolesObj is System.Collections.IEnumerable rolesList)
        {
            foreach (var r in rolesList)
            {
                if (r is Dictionary<string, object?> roleDict && 
                    roleDict.TryGetValue("role", out var roleVal) && 
                    roleVal is string roleName)
                {
                    roles.Add(roleName);
                }
            }
        }

        // 2. Fallback: If for some reason the child table is empty, fall back to querying the "Has Role" table directly
        if (roles.Count == 0)
        {
            try
            {
                var rolesFilter = new Dictionary<string, string>
                {
                    ["parent"] = email,
                    ["parenttype"] = "User"
                };
                var roleFields  = new List<string> { "role" };
                var roleRows    = await GetDocListAsync("Has Role", rolesFilter, roleFields, limit: 100);
                roles = roleRows
                    .Select(r => r.TryGetValue("role", out var v) ? v?.ToString() : null)
                    .Where(r => !string.IsNullOrEmpty(r))
                    .Cast<string>()
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch roles from Has Role child table as fallback for {Email}", email);
            }
        }

        return new ErpNextUser
        {
            Username  = userDoc.GetValueOrDefault("name")?.ToString() ?? email,
            Email     = email,
            FullName  = userDoc.GetValueOrDefault("full_name")?.ToString() ?? string.Empty,
            Roles     = roles,
            Phone     = userDoc.GetValueOrDefault("phone")?.ToString(),
            UserImage = userDoc.GetValueOrDefault("user_image")?.ToString()
        };
    }
    public async Task<List<Dictionary<string, object?>>> GetDocListViaMethodAsync(
    string doctype,
    Dictionary<string, string>? filters = null,
    List<string>? fields = null,
    int limit = 50)
    {
        var fieldsJson = fields is not null
            ? JsonSerializer.Serialize(fields)
            : "[\"name\"]";

        var filtersJson = filters is { Count: > 0 }
            ? JsonSerializer.Serialize(filters)
            : "{}";

        var url = "api/method/frappe.client.get_list" +
                  $"?doctype={Uri.EscapeDataString(doctype)}" +
                  $"&fields={Uri.EscapeDataString(fieldsJson)}" +
                  $"&filters={Uri.EscapeDataString(filtersJson)}" +
                  $"&limit_page_length={limit}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddAdminAuth(req);

        try
        {
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("ERPNext method get_list {Doctype} returned {Status}: {Body}", doctype, (int)resp.StatusCode, err);
                return [];
            }

            var body = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("message", out var msg) ||
                msg.ValueKind != JsonValueKind.Array)
                return [];

            return msg.EnumerateArray().Select(ParseElement).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERPNext GetDocListViaMethodAsync threw for {Doctype}", doctype);
            return [];
        }
    }
    public async Task<List<Dictionary<string, object?>>> GetDocListAsync(
        string doctype,
        Dictionary<string, string>? filters = null,
        List<string>? fields = null,
        int limit = 50,
        string? orderBy = null)
    {
        var fieldsJson  = fields is not null
            ? JsonSerializer.Serialize(fields)
            : "[\"name\"]";

        var filtersJson = "[]";
        if (filters is { Count: > 0 })
        {
            var filterList = filters
                .Select(kv => new[] { doctype, kv.Key, "=", kv.Value })
                .ToList();
            filtersJson = JsonSerializer.Serialize(filterList);
        }

        var url = $"api/resource/{Uri.EscapeDataString(doctype)}" +
                  $"?fields={Uri.EscapeDataString(fieldsJson)}" +
                  $"&filters={Uri.EscapeDataString(filtersJson)}" +
                  $"&limit={limit}";

        if (!string.IsNullOrEmpty(orderBy))
            url += $"&order_by={Uri.EscapeDataString(orderBy)}";

        return await GetListInternalAsync(url);
    }

    public async Task<Dictionary<string, object?>?> GetDocAsync(string doctype, string name)
    {
        var url = $"api/resource/{Uri.EscapeDataString(doctype)}/{Uri.EscapeDataString(name)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddAdminAuth(req);

        try
        {
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync();
            var doc  = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data))
                return null;

            return ParseElement(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERPNext GetDocAsync failed: {Doctype}/{Name}", doctype, name);
            return null;
        }
    }

    public async Task<List<Dictionary<string, object?>>> RunReportAsync(
        string reportName,
        Dictionary<string, string>? filters = null)
    {
        var filtersJson = filters is not null
            ? JsonSerializer.Serialize(filters)
            : "{}";

        var url = $"api/method/frappe.desk.query_report.run" +
                  $"?report_name={Uri.EscapeDataString(reportName)}" +
                  $"&filters={Uri.EscapeDataString(filtersJson)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddAdminAuth(req);

        try
        {
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("ERPNext RunReport {Report} failed: {Status} {Body}", reportName, (int)resp.StatusCode, err);
                return [];
            }

            var body = await resp.Content.ReadAsStringAsync();
            var doc  = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("message", out var msg)) return [];

            // Report returns columns + result rows
            if (!msg.TryGetProperty("result", out var rows) ||
                rows.ValueKind != JsonValueKind.Array)
                return [];

            if (!msg.TryGetProperty("columns", out var colsEl) ||
                colsEl.ValueKind != JsonValueKind.Array)
                return [];

            // Map column labels
            var cols = colsEl.EnumerateArray()
                .Select(c => c.TryGetProperty("fieldname", out var fn)
                    ? fn.GetString() ?? string.Empty
                    : c.GetString() ?? string.Empty)
                .ToList();

            var result = new List<Dictionary<string, object?>>();
            foreach (var row in rows.EnumerateArray())
            {
                var dict = new Dictionary<string, object?>();
                if (row.ValueKind == JsonValueKind.Array)
                {
                    var vals = row.EnumerateArray().ToList();
                    for (var i = 0; i < cols.Count && i < vals.Count; i++)
                        dict[cols[i]] = ParseValue(vals[i]);
                }
                result.Add(dict);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERPNext RunReportAsync threw for {Report}", reportName);
            return [];
        }
    }

    public async Task<string?> CallMethodAsync(string method, Dictionary<string, string>? args = null)
    {
        var qs = args is not null
            ? "?" + string.Join("&", args.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"))
            : string.Empty;

        var url = $"api/method/{method}{qs}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddAdminAuth(req);

        try
        {
            var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            return resp.IsSuccessStatusCode ? body : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERPNext CallMethodAsync threw for {Method}", method);
            return null;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<List<Dictionary<string, object?>>> GetListInternalAsync(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddAdminAuth(req);

        try
        {
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("ERPNext GET {Url} returned {Status}: {Body}", url, (int)resp.StatusCode, err);
                return [];
            }

            var body = await resp.Content.ReadAsStringAsync();
            var doc  = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
                return [];

            return data.EnumerateArray().Select(ParseElement).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERPNext GetList threw for {Url}", url);
            return [];
        }
    }

    private static Dictionary<string, object?> ParseElement(JsonElement el)
    {
        var dict = new Dictionary<string, object?>();
        if (el.ValueKind != JsonValueKind.Object) return dict;
        foreach (var prop in el.EnumerateObject())
            dict[prop.Name] = ParseValue(prop.Value);
        return dict;
    }

    private static object? ParseValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String  => el.GetString(),
        JsonValueKind.Number  => el.TryGetDouble(out var d) ? d : (object?)null,
        JsonValueKind.True    => true,
        JsonValueKind.False   => false,
        JsonValueKind.Null    => null,
        JsonValueKind.Array   => el.EnumerateArray().Select(ParseValue).ToList(),
        JsonValueKind.Object  => ParseElement(el),
        _                     => null
    };
}
