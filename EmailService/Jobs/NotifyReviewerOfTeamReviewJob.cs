using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using EmailService.Configs;
using EmailService.Services;

namespace EmailService.Jobs
{
    [DisallowConcurrentExecution]
    public class NotifyReviewerOfTeamReviewJob : IJob
    {
        private const long TeamReviewNotificationTemplateId = 5;

        private readonly ISupabaseService _supabaseService;
        private readonly IEmailService _emailService;
        private readonly AppConfig _appConfig;
        private readonly ILogger<NotifyReviewerOfTeamReviewJob> _logger;

        public NotifyReviewerOfTeamReviewJob(
            ISupabaseService supabaseService,
            IEmailService emailService,
            IOptions<AppConfig> appConfig,
            ILogger<NotifyReviewerOfTeamReviewJob> logger)
        {
            _supabaseService = supabaseService;
            _emailService = emailService;
            _appConfig = appConfig.Value;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            List<DTOs.TeamReviewNotificationInfo> reviews;
            try
            {
                reviews = await _supabaseService.GetTeamReviewsForReviewerNotificationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch team reviews for reviewer notification.");
                return;
            }

            foreach (var review in reviews)
            {
                try
                {
                    if (string.IsNullOrEmpty(review.ReviewerEmail))
                    {
                        _logger.LogWarning($"Missing reviewer email for team review ID {review.Id}");
                        continue;
                    }

                    // Build template parameters - these map to {{params.variableName}} in Brevo template
                    var templateParams = new Dictionary<string, string>
                    {
                        { "review_id", review.Id.ToString() },
                        { "review_url", $"{_appConfig.BaseUrl.TrimEnd('/')}/team-reviews/{review.Id}" }
                    };

                    var sent = await _emailService.SendTemplateEmailAsync(
                        review.ReviewerEmail,
                        TeamReviewNotificationTemplateId,
                        templateParams);

                    if (sent)
                    {
                        _logger.LogInformation($"Notified reviewer {review.ReviewerEmail} for team review {review.Id}");
                        await _supabaseService.MarkTeamReviewerNotifiedAsync(review.Id);
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to notify reviewer {review.ReviewerEmail} for team review {review.Id}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing team review {review.Id}");
                }
            }
        }
    }
}
