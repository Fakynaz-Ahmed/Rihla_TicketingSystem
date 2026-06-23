using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Rihla.Services.Cache;
using Rihla.Services.Db;

namespace Rihla.Middleware;

/// <summary>
/// JWT bearer event handlers.
/// Token validity: Redis read-through cache (60s TTL) → SQL Server DB.
/// This keeps per-request latency low while ensuring revoked tokens are rejected.
/// </summary>
public static class JwtAuthMiddleware
{
    private const string CachePrefix = "jwt:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public static JwtBearerEvents CreateEvents() => new()
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken))
                context.Token = accessToken;
            return Task.CompletedTask;
        },

        OnTokenValidated = async context =>
        {
            var jti = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (string.IsNullOrEmpty(jti))
            {
                context.Fail("Token is missing the JTI claim.");
                return;
            }

            var cache = context.HttpContext.RequestServices.GetRequiredService<ICacheService>();
            var cacheKey = $"{CachePrefix}{jti}";

            // Fast path: Redis cache hit
            if (await cache.ExistsAsync(cacheKey))
                return;

            // Slow path: DB lookup
            var tokenRepo = context.HttpContext.RequestServices.GetRequiredService<ITokenRepository>();
            var isActive = await tokenRepo.IsActiveAsync(jti);

            if (!isActive)
            {
                context.Fail("Token has been revoked or is no longer active.");
                return;
            }

            // Re-warm cache so next request is fast
            await cache.SetAsync(cacheKey, "1", CacheTtl);
        },

        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = "Unauthorized. A valid Bearer token is required."
            });
        },

        OnForbidden = context =>
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = "Forbidden. You do not have permission to access this resource."
            });
        }
    };
}
