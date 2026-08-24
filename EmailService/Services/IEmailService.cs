using System.Collections.Generic;
using System.Threading.Tasks;
using EmailService.Templates;

namespace EmailService.Services
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string to, string subject, string body, string fromDisplayName = "Boom Bust");

        /// <summary>
        /// Send an email using a Brevo template.
        /// </summary>
        /// <param name="to">Recipient email address</param>
        /// <param name="templateId">Brevo template ID</param>
        /// <param name="templateParams">Key-value pairs to substitute in the template</param>
        Task<bool> SendTemplateEmailAsync(string to, long templateId, Dictionary<string, string>? templateParams = null);

        /// <summary>
        /// Send an email using a template contract with validation and automatic ID resolution.
        /// </summary>
        /// <param name="contract">Validated template contract</param>
        /// <returns>
        /// Success plus Brevo's message ID. The ID is persisted as
        /// <c>EmailDeliveryLog.ExternalId</c>, which is what delivery webhooks are
        /// matched against — a send recorded without it can never be reconciled.
        /// </returns>
        Task<SendResult> SendTemplateEmailAsync(ITemplateContract contract);
    }
}
