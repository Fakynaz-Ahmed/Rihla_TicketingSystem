using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using StackExchange.Redis;
using Rihla.Config;
using Rihla.Data;
using Rihla.Middleware;
using Rihla.Services.Ai;
using Rihla.Services.Auth;
using Rihla.Services.Cache;
using Rihla.Services.Db;
using Rihla.Services.Jwt;
using Rihla.Services.ErpNext;
using Rihla.Services.ErpNext.Tools;
using Rihla.Filters;
using Rihla.Services.Storage;
using Rihla.WebSockets;
using System.Globalization;

// Load .env file into environment variables
var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFile))
{
    foreach (var line in File.ReadAllLines(envFile))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
        var eq = trimmed.IndexOf('=');
        if (eq < 0) continue;
        Environment.SetEnvironmentVariable(trimmed[..eq].Trim(), trimmed[(eq + 1)..].Trim());
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// -------------------------------------------------------------------------
// Bind configuration sections
// -------------------------------------------------------------------------
var jwtSettings = builder.Configuration
    .GetSection(JwtSettings.SectionName)
    .Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT settings are not configured.");

var erpNextSettings = builder.Configuration
    .GetSection(ErpNextSettings.SectionName)
    .Get<ErpNextSettings>()
    ?? throw new InvalidOperationException("ERPNext settings are not configured.");

var redisSettings = builder.Configuration
    .GetSection(RedisSettings.SectionName)
    .Get<RedisSettings>()
    ?? throw new InvalidOperationException("Redis settings are not configured.");

var dbSettings = builder.Configuration
    .GetSection(DatabaseSettings.SectionName)
    .Get<DatabaseSettings>()
    ?? throw new InvalidOperationException("Database settings are not configured.");

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<ErpNextSettings>(builder.Configuration.GetSection(ErpNextSettings.SectionName));
builder.Services.Configure<RedisSettings>(builder.Configuration.GetSection(RedisSettings.SectionName));
builder.Services.Configure<OpenAiSettings>(builder.Configuration.GetSection(OpenAiSettings.SectionName));
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection(MongoSettings.SectionName));

// -------------------------------------------------------------------------
// Localization — X-Language: ar | en
// -------------------------------------------------------------------------
builder.Services.AddLocalization(options => options.ResourcesPath = string.Empty);

// -------------------------------------------------------------------------
// Controllers & Swagger
// -------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Rihla API (ERPNext)", Version = "v1" });

    // Include XML doc comments in Swagger UI
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) options.IncludeXmlComments(xmlPath);

    // JWT Bearer auth button
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access_token from POST /api/auth/login"
    });

    options.OperationFilter<LanguageHeaderOperationFilter>();

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    });
});

// -------------------------------------------------------------------------
// CORS — allow any origin for development / testing
// -------------------------------------------------------------------------
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

// -------------------------------------------------------------------------
// WebSocket hub
// -------------------------------------------------------------------------
builder.Services.AddSingleton<WebSocketHub>();
builder.Services.AddSingleton<IWebSocketHub>(sp => sp.GetRequiredService<WebSocketHub>());

// -------------------------------------------------------------------------
// JWT Authentication
// -------------------------------------------------------------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = "role",
            NameClaimType = "name"
        };
        options.Events = JwtAuthMiddleware.CreateEvents();
    });

builder.Services.AddAuthorization();

// -------------------------------------------------------------------------
// SQL Server — Entity Framework Core
// -------------------------------------------------------------------------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(dbSettings.ConnectionString));

builder.Services.AddScoped<IAppUserRepository, AppUserRepository>();
builder.Services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
builder.Services.AddScoped<ITokenRepository, TokenRepository>();
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IPassportDataRepository, PassportDataRepository>();

// -------------------------------------------------------------------------
// MongoDB — messages collection
// -------------------------------------------------------------------------
builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration["MongoDB:ConnectionString"] ?? "mongodb://localhost:27017"));

builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddHostedService<MongoIndexInitializer>();

// -------------------------------------------------------------------------
// Redis
// -------------------------------------------------------------------------
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse(redisSettings.ConnectionString);
    options.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(options);
});

builder.Services.AddSingleton<ICacheService, CacheService>();

// -------------------------------------------------------------------------
// Http Clients & Services
// -------------------------------------------------------------------------
builder.Services.AddHttpClient<IErpNextClient, ErpNextClient>();
builder.Services.AddHttpClient<IAiService, AiService>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IErpNextToolExecutor, ErpNextToolExecutor>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IErpNextSyncService, ErpNextSyncService>();
builder.Services.AddScoped<IStorageService, LocalStorageService>();
builder.Services.AddHostedService<ErpNextSyncBackgroundService>();

// -------------------------------------------------------------------------
// Build & configure pipeline
// -------------------------------------------------------------------------
var app = builder.Build();

// Auto-apply EF Core migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

    var retries = 10;
    while (retries > 0)
    {
        try
        {
            logger.LogInformation("Applying database migrations...");
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
            break;
        }
        catch (Exception ex)
        {
            retries--;
            if (retries == 0)
            {
                logger.LogCritical(ex, "Database migration failed after all retries. Shutting down.");
                throw;
            }
            logger.LogWarning(ex, "Database not ready. Retrying in 5 seconds... ({Retries} retries left)", retries);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Rihla API v1");
    options.RoutePrefix = "swagger";
});

app.UseCors();

var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("ar") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en"),
    SupportedCultures     = supportedCultures,
    SupportedUICultures   = supportedCultures,
    RequestCultureProviders = [new HeaderRequestCultureProvider()]
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseWebSockets();
app.UseMiddleware<WebSocketMiddleware>();
app.UseAuthentication();
app.UseMiddleware<UserActivityMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>
/// Custom localization provider that reads the preferred language from the 'X-Language' header.
/// </summary>
public class HeaderRequestCultureProvider : Microsoft.AspNetCore.Localization.RequestCultureProvider
{
    public override Task<Microsoft.AspNetCore.Localization.ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        if (httpContext == null) throw new ArgumentNullException(nameof(httpContext));
        
        var lang = httpContext.Request.Headers["X-Language"].ToString();
        if (string.IsNullOrEmpty(lang))
        {
            return NullProviderCultureResult;
        }
        
        return Task.FromResult<Microsoft.AspNetCore.Localization.ProviderCultureResult?>(
            new Microsoft.AspNetCore.Localization.ProviderCultureResult(lang));
    }
}
