using Quartz;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
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

        public SendTradeReviewResults(
            IOptions<GmailConfig> gmailConfig,
            ISupabaseService supabaseService)
        {
            _gmailConfig = gmailConfig.Value;
            _supabaseService = supabaseService;
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
                            Console.WriteLine($"Attempting to send email from {_gmailConfig.Username} to {trade.UserEmail} using IP-based authentication...");
                            await smtp.SendMailAsync(mail);
                            Console.WriteLine("Email sent successfully via Google Workspace SMTP Relay!");
                            emailSent = true;
                        }
                    }
                }
                catch (SmtpException ex)
                {
                    Console.WriteLine($"SMTP Error: {ex.StatusCode} - {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                    }
                    Console.WriteLine("Troubleshooting Tip: Double-check the public IP address configured in your Google Workspace SMTP Relay settings.");
                    Console.WriteLine("Also, ensure your server's firewall allows outbound connections on port 587.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An unexpected error occurred: {ex.Message}");
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
                                Console.WriteLine($"Failed to mark email as sent for trade {trade.TradeId} after {maxRetries} attempts: {ex.Message}");
                            }
                            else
                            {
                                await Task.Delay(1000 * attempt); // Exponential backoff
                            }
                        }
                    }
                }

                Console.WriteLine($"[{DateTime.Now}] Sent trade completion email to {trade.UserEmail} for trade {trade.TradeId}.");
            }
        }
    }
}
