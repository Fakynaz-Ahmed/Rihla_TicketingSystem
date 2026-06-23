using System.Security.Claims;
using Rihla.Services.Db;

namespace Rihla.Middleware;

public class UserActivityMiddleware
{
    private readonly RequestDelegate _next;

    public UserActivityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IServiceProvider serviceProvider)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userType = context.User.FindFirst("user_type")?.Value;
            
            if (userType == "employee" || userType == "superadmin")
            {
                var odooUserIdClaim = context.User.FindFirst("sub")?.Value 
                                   ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (int.TryParse(odooUserIdClaim, out int odooUserId))
                {
                    using var scope = serviceProvider.CreateScope();
                    var userRepo = scope.ServiceProvider.GetRequiredService<IAppUserRepository>();
                    await userRepo.UpdateLastActivityAsync(odooUserId);
                }
            }
        }

        await _next(context);
    }
}
