using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Quartz;
using Supabase;
using BoomBust.Logging;
using EmailService;
using EmailService.Configs;
using EmailService.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Logging
builder.Host.UseBoomBustLogging(options =>
{
    options.ApplicationName = "EmailService";
    options.LogFilePath = "logs/email-service-.txt";
    options.OverrideToWarning = new[] { "Microsoft", "System", "Quartz" };
});

// Configuration options
builder.Services.Configure<BrevoConfig>(builder.Configuration.GetSection("Brevo"));
builder.Services.Configure<SupabaseConfig>(builder.Configuration.GetSection("Supabase"));
builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("App"));

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
builder.Services.AddScoped<EmailService.Services.IEmailService, EmailService.Services.BrevoEmailService>();
builder.Services.AddScoped<EmailService.Services.ReviewEmailFactory>();

// Background services
builder.Services.AddHostedService<QueueConsumerService>();

// Quartz for legacy jobs (will be migrated to queue-based in future tickets)
builder.Services.AddQuartz(q =>
{
    q.ScheduleJob<EmailService.Jobs.NotifyReviewerOfTeamReviewJob>(trigger => trigger
        .WithIdentity("notifyReviewerOfTeamReviewTrigger", "emailJobs")
        .StartNow()
        .WithSimpleSchedule(x => x
            .WithIntervalInMinutes(1)
            .RepeatForever())
    );
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<QueueConsumerHealthCheck>("queue_consumer");

// API services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Initialize Supabase
var supabaseClient = app.Services.GetRequiredService<Client>();
await supabaseClient.InitializeAsync();

// Configure HTTP pipeline
app.UseRouting();

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

app.MapControllers();

app.Logger.LogInformation("Email service starting with web host + queue consumer runtime");

await app.RunAsync();
