using System.Text.Json.Serialization;

namespace Rihla.DTOs;

public class EmployeeFilterDto
{
    /// <summary>Partial match on name or email.</summary>
    [JsonPropertyName("search")]
    public string? Search { get; set; }

    /// <summary>Exact match on department name (e.g. "Customer Care", "Maintenance").</summary>
    [JsonPropertyName("department")]
    public string? Department { get; set; }

    /// <summary>Filter by role: superadmin | employee</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; } = 20;
}

public class CustomerFilterDto
{
    /// <summary>Partial match on name, email, or phone.</summary>
    [JsonPropertyName("search")]
    public string? Search { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; } = 20;
}

public class EmployeeDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("odoo_user_id")]
    public int OdooUserId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("employee_role")]
    public string? EmployeeRole { get; set; }

    /// <summary>superadmin | employee</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("last_login_at")]
    public DateTime LastLoginAt { get; set; }

    [JsonPropertyName("total_active_assigned_tickets")]
    public int TotalActiveAssignedTickets { get; set; }

    [JsonPropertyName("total_active_assigned_visits")]
    public int TotalActiveAssignedVisits { get; set; }

    [JsonPropertyName("is_online")]
    public bool IsOnline { get; set; }
}

public class CustomerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("odoo_user_id")]
    public int OdooUserId { get; set; }

    [JsonPropertyName("partner_id")]
    public int PartnerId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    /// <summary>Always "customer" for portal users.</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = "customer";

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("last_login_at")]
    public DateTime LastLoginAt { get; set; }

    [JsonPropertyName("street")]
    public string? Street { get; set; }

    [JsonPropertyName("street2")]
    public string? Street2 { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }
}

public class CustomerDetailsDto : CustomerDto
{
    [JsonPropertyName("total_machines")]
    public int TotalMachines { get; set; }
    [JsonPropertyName("total_tickets")]
    public int TotalTickets { get; set; }
    [JsonPropertyName("total_visits")]
    public int TotalVisits { get; set; }
}

public class EmployeeDetailsDto : EmployeeDto
{
    [JsonPropertyName("total_assigned_tickets")]
    public int TotalAssignedTickets { get; set; }
    [JsonPropertyName("total_assigned_visits")]
    public int TotalAssignedVisits { get; set; }
}
