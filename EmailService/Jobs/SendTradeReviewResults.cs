using Quartz;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using MimeKit;
using EmailService.Configs;
using EmailService.Services;
using EmailService.SupabaseModels;
using System.Collections.Generic;
using System.Net.Mail;

namespace EmailService.Jobs
{
    [DisallowConcurrentExecution]
    public class SendTradeReviewResults : IJob
    {
        private readonly GmailConfig _gmailConfig;
        private readonly ISupabaseService _supabaseService;
        private readonly ILogger<SendTradeReviewResults> _logger;

        public SendTradeReviewResults(
            IOptions<GmailConfig> gmailConfig,
            ISupabaseService supabaseService,
            ILogger<SendTradeReviewResults> logger)
        {
            _gmailConfig = gmailConfig.Value;
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
                bool emailSent = false;
                try
                {
                    using (MailMessage mail = new MailMessage())
                    {
                        mail.From = new MailAddress(_gmailConfig.Username, "Boom Bust Trade Review");
                        mail.To.Add(trade.UserEmail);
                        mail.Subject = "Your Trade Review Has Been Completed";
                        mail.Body = $"Hello, a trade of yours has been reviewed. \nYou can view the trade here: https://boombustfantasy.com/trade/{trade.TradeId} \nThank you so much for using Boom Bust!";
                        mail.IsBodyHtml = false;
                        using (System.Net.Mail.SmtpClient smtp = new System.Net.Mail.SmtpClient("smtp-relay.gmail.com", 587))
                        {
                            smtp.EnableSsl = true;
                            smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                            smtp.UseDefaultCredentials = false;
                            _logger.LogInformation("Attempting to send email from {Username} to {Email} using IP-based authentication", _gmailConfig.Username, trade.UserEmail);
                            await smtp.SendMailAsync(mail);
                            _logger.LogInformation("Email sent successfully via Google Workspace SMTP Relay for trade {TradeId} to {Email}", trade.TradeId, trade.UserEmail);
                            emailSent = true;
                        }
                    }
                }
                catch (SmtpException ex)
                {
                    _logger.LogError(ex, "SMTP error {StatusCode} sending trade completion email to {Email} for trade {TradeId}. Verify the sender IP is authorised in Google Workspace SMTP Relay and that port 587 is open outbound", ex.StatusCode, trade.UserEmail, trade.TradeId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error sending trade completion email to {Email} for trade {TradeId}", trade.UserEmail, trade.TradeId);
                }

                if (emailSent)
                {
                    // Mark email as sent with retries
                    const int maxRetries = 3;
                    int attempt = 0;
                    bool success = false;
                    while (attempt < maxRetries && !success)
                    {
                        try
                        {
                            await _supabaseService.MarkEmailSentAsync(trade.TradeId);
                            success = true;
                        }
                        catch (Exception ex)
                        {
                            attempt++;
                            if (attempt >= maxRetries)
                            {
                                _logger.LogError(ex, "Failed to mark email as sent for trade {TradeId} after {MaxRetries} attempts", trade.TradeId, maxRetries);
                            }
                            else
                            {
                                await Task.Delay(1000 * attempt); // Exponential backoff
                            }
                        }
                    }
                }

                _logger.LogInformation("Processed trade completion email for trade {TradeId} to {Email}", trade.TradeId, trade.UserEmail);
            }
        }
    }
}
