namespace Rihla.Config;

public class JwtSettings
{
    public const string SectionName = "Jwt";
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpirationDays { get; set; } = 7;
    public int RefreshTokenDays { get; set; } = 30;
}

public class ErpNextSettings
{
    public const string SectionName = "ErpNext";
    /// <summary>Base URL e.g. http://erpnext.envsabqpro.site</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Admin API Key for server-to-server calls.</summary>
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>Admin API Secret for server-to-server calls.</summary>
    public string ApiSecret { get; set; } = string.Empty;
    /// <summary>Frequency of background synchronization in hours.</summary>
    public int SyncIntervalHours { get; set; } = 6;
    /// <summary>Whether background sync is enabled.</summary>
    public bool EnableBackgroundSync { get; set; } = true;
}

public class OpenAiSettings
{
    public const string SectionName = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.3;
    public string SystemPrompt { get; set; } =
        "You are a smart business assistant for a company using ERPNext. " +
        "You have access to tools that can fetch real-time data from the company's ERP system. " +
        "Always use the appropriate tool to get accurate data before answering questions about sales, " +
        "purchases, inventory, employees, or financials. " +
        "Be concise and professional. Respond in the same language the user uses (Arabic or English).";
}

public class RedisSettings
{
    public const string SectionName = "Redis";
    public string ConnectionString { get; set; } = string.Empty;
    public string InstanceName { get; set; } = "Rihla:";
}

public class DatabaseSettings
{
    public const string SectionName = "Database";
    public string ConnectionString { get; set; } = string.Empty;
}

public class MongoSettings
{
    public const string SectionName = "MongoDB";
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "erp_chat";
}
