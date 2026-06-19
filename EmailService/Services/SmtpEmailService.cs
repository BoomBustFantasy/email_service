using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using EmailService.Configs;

namespace EmailService.Services
{
    public class SmtpEmailService : IEmailService
    {
        private readonly GmailConfig _config;
        private readonly ILogger<SmtpEmailService> _logger;

        public SmtpEmailService(IOptions<GmailConfig> config, ILogger<SmtpEmailService> logger)
        {
            _config = config.Value;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(string to, string subject, string body, string fromDisplayName = "Boom Bust Trade Review")
        {
            try
            {
                using (MailMessage mail = new MailMessage())
                {
                    mail.From = new MailAddress(_config.Username, fromDisplayName);
                    mail.To.Add(to);
                    mail.Subject = subject;
                    mail.Body = body;
                    mail.IsBodyHtml = false;
                    using (SmtpClient smtp = new SmtpClient("smtp-relay.gmail.com", 587))
                    {
                        smtp.EnableSsl = true;
                        smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                        smtp.UseDefaultCredentials = false;
                        // No credentials for IP-based relay
                        await smtp.SendMailAsync(mail);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMTP send failed to {Email}", to);
                return false;
            }
        }
    }
}
