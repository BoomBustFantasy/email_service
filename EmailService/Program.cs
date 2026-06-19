using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Quartz;
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

        services.AddScoped<EmailService.Services.ISupabaseService, EmailService.Services.SupabaseService>();
        services.AddScoped<EmailService.Services.IEmailService, EmailService.Services.SmtpEmailService>();

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

await host.RunAsync();
