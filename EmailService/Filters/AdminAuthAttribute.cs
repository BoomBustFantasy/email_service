using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using EmailService.Configs;

namespace EmailService.Filters;

/// <summary>
/// Attribute for admin API key authentication
/// </summary>
public class AdminAuthAttribute : TypeFilterAttribute
{
    public AdminAuthAttribute() : base(typeof(AdminAuthFilter))
    {
    }
}

/// <summary>
/// Filter that validates admin API key
/// </summary>
public class AdminAuthFilter : IAuthorizationFilter
{
    private readonly AppConfig _appConfig;
    private readonly ILogger<AdminAuthFilter> _logger;

    public AdminAuthFilter(IOptions<AppConfig> appConfig, ILogger<AdminAuthFilter> logger)
    {
        _appConfig = appConfig.Value;
        _logger = logger;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Get the admin API key from header
        if (!context.HttpContext.Request.Headers.TryGetValue("X-Admin-Api-Key", out var providedKey))
        {
            _logger.LogWarning("Admin API access denied - missing X-Admin-Api-Key header");
            context.Result = new UnauthorizedObjectResult(new { error = "Admin API key required" });
            return;
        }

        // Validate the key
        if (string.IsNullOrWhiteSpace(_appConfig.AdminApiKey))
        {
            _logger.LogError("Admin API key not configured in appsettings");
            context.Result = new StatusCodeResult(500);
            return;
        }

        if (providedKey != _appConfig.AdminApiKey)
        {
            _logger.LogWarning("Admin API access denied - invalid key");
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid admin API key" });
            return;
        }

        _logger.LogDebug("Admin API access granted");
    }
}
