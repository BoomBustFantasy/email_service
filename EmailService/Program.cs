using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Quartz;
using Supabase;
using BoomBust.Logging;
using EmailService;
using EmailService.Configs;


IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config.AddEnvironmentVariables();
    })
    .UseBoomBustLogging(options =>
    {
        options.ApplicationName = "EmailService";
        options.LogFilePath = "logs/email-service-.txt";
        options.OverrideToWarning = new[] { "Microsoft", "System", "Quartz" };
    })
    .ConfigureServices((hostContext, services) =>
    {
        services.Configure<GmailConfig>(hostContext.Configuration.GetSection("Gmail"));
        services.Configure<SupabaseConfig>(hostContext.Configuration.GetSection("Supabase"));
        services.Configure<AppConfig>(hostContext.Configuration.GetSection("App"));

        services.AddSingleton(sp =>
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

        services.AddScoped<EmailService.Services.ISupabaseService, EmailService.Services.SupabaseService>();
        services.AddScoped<EmailService.Services.IEmailService, EmailService.Services.SmtpEmailService>();
        services.AddScoped<EmailService.Services.ReviewEmailFactory>();

        services.AddQuartz(q =>
        {
            q.ScheduleJob<EmailService.Jobs.SendTradeReviewResults>(trigger => trigger
                .WithIdentity("sendTradeReviewResultsTrigger", "emailJobs")
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInMinutes(1)
                    .RepeatForever())
            );
            q.ScheduleJob<EmailService.Jobs.SendTeamReviewResults>(trigger => trigger
                .WithIdentity("sendTeamReviewResultsTrigger", "emailJobs")
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInMinutes(1)
                    .RepeatForever())
            );
            q.ScheduleJob<EmailService.Jobs.NotifyReviewerOfTradeJob>(trigger => trigger
                .WithIdentity("notifyReviewerOfTradeTrigger", "emailJobs")
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInMinutes(1)
                    .RepeatForever())
            );
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
    })
    .Build();

var supabaseClient = host.Services.GetRequiredService<Client>();
await supabaseClient.InitializeAsync();

await host.RunAsync();
