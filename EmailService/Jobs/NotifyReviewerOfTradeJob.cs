using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using EmailService.Services;

namespace EmailService.Jobs
{
    [DisallowConcurrentExecution]
    public class NotifyReviewerOfTradeJob : IJob
    {
        private readonly ISupabaseService _supabaseService;
        private readonly IEmailService _emailService;
        private readonly ReviewEmailFactory _emailFactory;
        private readonly ILogger<NotifyReviewerOfTradeJob> _logger;

        public NotifyReviewerOfTradeJob(ISupabaseService supabaseService, IEmailService emailService, ReviewEmailFactory emailFactory, ILogger<NotifyReviewerOfTradeJob> logger)
        {
            _supabaseService = supabaseService;
            _emailService = emailService;
            _emailFactory = emailFactory;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            List<(long Id, string Email)> trades;
            try
            {
                trades = await _supabaseService.GetTradesForReviewerNotificationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch trades for reviewer notification.");
                return;
            }

            foreach (var trade in trades)
            {
                try
                {
                    if (string.IsNullOrEmpty(trade.Email))
                    {
                        _logger.LogWarning($"Missing reviewer email for trade ID {trade.Id}");
                        continue;
                    }

                    var message = _emailFactory.BuildReviewerNotification(trade.Id);
                    var sent = await _emailService.SendEmailAsync(trade.Email, message.Subject, message.Body, "Boom Bust Reviewer Notification");

                    if (sent)
                    {
                        _logger.LogInformation($"Notified reviewer {trade.Email} for trade {trade.Id}");
                        await _supabaseService.MarkReviewerNotifiedAsync(trade.Id);
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to notify reviewer {trade.Email} for trade {trade.Id}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing trade {trade.Id}");
                }
            }
        }
    }
}
