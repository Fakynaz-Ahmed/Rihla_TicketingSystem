using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Rihla.DTOs;

public class LoginRequestDto
{
    [Required]
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public class RefreshTokenRequestDto
{
    [Required]
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;
}

public class ChangePasswordRequestDto
{
    [Required]
    [JsonPropertyName("old_password")]
    public string OldPassword { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("new_password")]
    public string NewPassword { get; set; } = string.Empty;
}

//public class LoginResponseDto
//{
//    [JsonPropertyName("access_token")]
//    public string AccessToken { get; set; } = string.Empty;

//    [JsonPropertyName("refresh_token")]
//    public string RefreshToken { get; set; } = string.Empty;

//    [JsonPropertyName("expires_at")]
//    public DateTime ExpiresAt { get; set; }

//    [JsonPropertyName("user")]
//    public UserInfoDto User { get; set; } = new();
//}


//public class AdminChangePasswordRequestDto
//{
//    /// <summary>Local DB ID of the user whose password is being changed.</summary>
//    [Required]
//    public int UserId { get; set; }

//    [Required]
//    [MinLength(6)]
//    public string NewPassword { get; set; } = string.Empty;
//}
