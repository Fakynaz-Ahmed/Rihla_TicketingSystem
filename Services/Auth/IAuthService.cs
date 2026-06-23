using Rihla.DTOs;

namespace Rihla.Services.Auth;

public interface IAuthService
{
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
    Task<LoginResponseDto> RefreshTokenAsync(string refreshToken);
    Task LogoutAsync(string token);
    Task ChangePasswordAsync(int userId, string oldPassword, string newPassword);
}
