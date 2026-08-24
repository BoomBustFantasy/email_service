using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Supabase;
using BoomBust.Logging;
using EmailService;
using EmailService.Configs;
using EmailService.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration.
//
// appsettings.Defaults.json carries the non-secret settings and ships inside the
// image. appsettings.json is gitignored because it holds credentials, so it is
// not in the Docker build context — anything defined only there binds to nothing
// in production. That is how TemplateIdMap arrived empty in prod and every
// template failed the startup validator at once.
//
// Order is precedence: defaults, then local appsettings.json, then environment
// variables (how the container supplies secrets).
builder.Configuration
    .AddJsonFile("appsettings.Defaults.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Logging
builder.Host.UseBoomBustLogging(options =>
{
    options.ApplicationName = "EmailService";
    options.LogFilePath = "logs/email-service-.txt";
    options.OverrideToWarning = new[] { "Microsoft", "System" };
});

// Configuration options
builder.Services.Configure<BrevoConfig>(builder.Configuration.GetSection("Brevo"));
builder.Services.Configure<SupabaseConfig>(builder.Configuration.GetSection("Supabase"));
builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("App"));
builder.Services.Configure<EmailService.Templates.TemplateConfig>(builder.Configuration.GetSection("Templates"));
builder.Services.Configure<EmailService.Configs.QueueReliabilityConfig>(builder.Configuration.GetSection("QueueReliability"));

// Supabase client
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IOptions<SupabaseConfig>>().Value;
    if (string.IsNullOrEmpty(config.Url) || string.IsNullOrEmpty(config.ServiceRoleKey))
        throw new InvalidOperationException("Supabase configuration is missing");

    return new Client(config.Url, config.ServiceRoleKey, new SupabaseOptions
    {
        AutoConnectRealtime = false,
        AutoRefreshToken = false
    });
});

// Application services
builder.Services.AddScoped<EmailService.Services.ISupabaseService, EmailService.Services.SupabaseService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<EmailService.Templates.ITemplateService, EmailService.Templates.TemplateService>();
builder.Services.AddSingleton<EmailService.Templates.ITemplateContractRegistry, EmailService.Templates.TemplateContractRegistry>();
builder.Services.AddSingleton<EmailService.Services.QueueMetrics>();
builder.Services.AddScoped<EmailService.Services.IEmailService, EmailService.Services.BrevoEmailService>();
builder.Services.AddScoped<EmailService.Services.ReviewEmailFactory>();

// Background services
builder.Services.AddHostedService<QueueConsumerService>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<QueueConsumerHealthCheck>("queue_consumer");

// API services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Refuse to start on incomplete template wiring rather than discovering it one
// dropped or endlessly-retried message at a time.
EmailService.Templates.TemplateConfigurationValidator.Validate(
    app.Services.GetRequiredService<EmailService.Templates.ITemplateContractRegistry>(),
    app.Services.GetRequiredService<IOptions<EmailService.Templates.TemplateConfig>>().Value);

// Initialize Supabase
var supabaseClient = app.Services.GetRequiredService<Client>();
await supabaseClient.InitializeAsync();

// Configure HTTP pipeline
app.UseRouting();

// Brevo webhook signatures are computed over the raw request body, which model
// binding consumes before the controller runs. Buffer it so the HMAC check can
// rewind and re-read it.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/webhooks"))
    {
        context.Request.EnableBuffering();
    }

    await next();
});

// Health check endpoint
app.MapHealthChecks("/health");

// Basic status endpoint
app.MapGet("/", () => new
{
    service = "email_service",
    version = "1.0.0",
    status = "running",
    timestamp = DateTime.UtcNow
});

// Queue metrics endpoint
app.MapGet("/metrics", (EmailService.Services.QueueMetrics metrics) =>
{
    return Results.Ok(new
    {
        queue_metrics = metrics.GetSnapshot(),
        timestamp = DateTime.UtcNow
    });
});

app.MapControllers();

app.Logger.LogInformation("Email service starting with web host + queue consumer runtime");

await app.RunAsync();
