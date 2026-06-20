using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using EmailService.Services;

namespace EmailService.Jobs
{
    [DisallowConcurrentExecution]
    public class NotifyReviewerOfTeamReviewJob : IJob
    {
        private readonly ISupabaseService _supabaseService;
        private readonly IEmailService _emailService;
        private readonly ReviewEmailFactory _emailFactory;
        private readonly ILogger<NotifyReviewerOfTeamReviewJob> _logger;

        public NotifyReviewerOfTeamReviewJob(ISupabaseService supabaseService, IEmailService emailService, ReviewEmailFactory emailFactory, ILogger<NotifyReviewerOfTeamReviewJob> logger)
        {
            _supabaseService = supabaseService;
            _emailService = emailService;
            _emailFactory = emailFactory;
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

                    var message = _emailFactory.BuildTeamReviewerNotification(review.Id);
                    var sent = await _emailService.SendEmailAsync(review.ReviewerEmail, message.Subject, message.Body, "Boom Bust Team Review");

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
