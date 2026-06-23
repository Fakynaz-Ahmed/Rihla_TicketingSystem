using System.Text.Json.Serialization;

namespace Rihla.DTOs;

//public class LoginResponseDto
//{
//    [JsonPropertyName("access_token")]
//    public string AccessToken { get; set; } = string.Empty;

//    [JsonPropertyName("refresh_token")]
//    public string RefreshToken { get; set; } = string.Empty;

//    [JsonPropertyName("token_type")]
//    public string TokenType { get; set; } = "Bearer";

//    [JsonPropertyName("expires_at")]
//    public DateTime ExpiresAt { get; set; }

//    [JsonPropertyName("user")]
//    public UserInfoDto User { get; set; } = new();
//}

//public class UserInfoDto
//{
//    public int Id { get; set; }
//    public string Name { get; set; } = string.Empty;
//    public string Email { get; set; } = string.Empty;

//    [JsonPropertyName("avatar_url")]
//    public string? AvatarUrl { get; set; }

//    [JsonPropertyName("user_type")]
//    public string UserType { get; set; } = string.Empty;

//    [JsonPropertyName("employee_role")]
//    public string? EmployeeRole { get; set; }

//    [JsonPropertyName("department")]
//    public string? Department { get; set; }

//    public PartnerInfoDto? Partner { get; set; }
//    public EmployeeInfoDto? Employee { get; set; }
//}

public class PartnerInfoDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string Street2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class EmployeeInfoDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;
    public string WorkPhone { get; set; } = string.Empty;
}
