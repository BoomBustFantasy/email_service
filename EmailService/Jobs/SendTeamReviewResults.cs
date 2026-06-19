using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EmailService.SupabaseModels;
using EmailService.Services;
using Quartz;
using EmailService.DTOs;

namespace EmailService.Jobs
{
    [DisallowConcurrentExecution]
    public class SendTeamReviewResults : IJob
    {
        private readonly ISupabaseService _supabaseService;
        private readonly IEmailService _emailService;
        private readonly ILogger<SendTeamReviewResults> _logger;

        public SendTeamReviewResults(ISupabaseService supabaseService, IEmailService emailService, ILogger<SendTeamReviewResults> logger)
        {
            _supabaseService = supabaseService;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            List<TeamReviewEmailInfo> reviews;
            try
            {
                reviews = await _supabaseService.GetPendingTeamReviewEmailsWithYoutubeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch pending team reviews with YouTube links.");
                return;
            }

            foreach (var review in reviews)
            {
                try
                {
                    if (string.IsNullOrEmpty(review.Email))
                    {
                        _logger.LogWarning($"Missing email for review ID {review.Id}");
                        continue;
                    }

                    var subject = $"Your Team Review is Ready!";
                    var body = $"Hello,\n\nYour team review is ready. You can view the YouTube link here: {review.YoutubeLink}\n\nThank you!";
                    var sent = await _emailService.SendEmailAsync(review.Email, subject, body, "Boom Bust Team Review");

                    if (sent)
                    {
                        await _supabaseService.MarkTeamReviewEmailedAsync(review.Id);
                        _logger.LogInformation($"Emailed team review {review.Id} to {review.Email}");
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to send email for team review {review.Id} to {review.Email}");
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
