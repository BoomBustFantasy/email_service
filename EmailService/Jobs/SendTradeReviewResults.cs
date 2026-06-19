using Quartz;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EmailService.Services;
using EmailService.SupabaseModels;
using System.Collections.Generic;

namespace EmailService.Jobs
{
    [DisallowConcurrentExecution]
    public class SendTradeReviewResults : IJob
    {
        private readonly IEmailService _emailService;
        private readonly ISupabaseService _supabaseService;
        private readonly ILogger<SendTradeReviewResults> _logger;

        public SendTradeReviewResults(
            IEmailService emailService,
            ISupabaseService supabaseService,
            ILogger<SendTradeReviewResults> logger)
        {
            _emailService = emailService;
            _supabaseService = supabaseService;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            // Fetch completed trades with users
            var completedTrades = await _supabaseService.GetCompletedTradeReviewsWithUsersAsync();

            foreach (var trade in completedTrades)
            {
                if (string.IsNullOrWhiteSpace(trade.UserEmail)) continue;

                var emailSent = await _emailService.SendEmailAsync(
                    trade.UserEmail,
                    "Your Trade Review Has Been Completed",
                    $"Hello, a trade of yours has been reviewed. \nYou can view the trade here: https://boombustfantasy.com/trade/{trade.TradeId} \nThank you so much for using Boom Bust!",
                    "Boom Bust Trade Review");

                if (emailSent)
                {
                    await _supabaseService.MarkEmailSentAsync(trade.TradeId);
                }

                _logger.LogInformation("Processed trade completion email for trade {TradeId} to {Email}", trade.TradeId, trade.UserEmail);
            }
        }
    }
}
