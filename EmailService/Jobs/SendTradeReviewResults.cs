using Quartz;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EmailService.Services;

namespace EmailService.Jobs
{
    [DisallowConcurrentExecution]
    public class SendTradeReviewResults : IJob
    {
        private readonly ISupabaseService _supabaseService;
        private readonly IEmailService _emailService;
        private readonly ReviewEmailFactory _emailFactory;
        private readonly ILogger<SendTradeReviewResults> _logger;

        public SendTradeReviewResults(
            ISupabaseService supabaseService,
            IEmailService emailService,
            ReviewEmailFactory emailFactory,
            ILogger<SendTradeReviewResults> logger)
        {
            _supabaseService = supabaseService;
            _emailService = emailService;
            _emailFactory = emailFactory;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var completedTrades = await _supabaseService.GetCompletedTradeReviewsWithUsersAsync();

            foreach (var trade in completedTrades)
            {
                if (string.IsNullOrWhiteSpace(trade.UserEmail)) continue;

                var message = _emailFactory.BuildTradeReviewCompleted(trade.TradeId);
                var emailSent = await _emailService.SendEmailAsync(
                    trade.UserEmail,
                    message.Subject,
                    message.Body,
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
