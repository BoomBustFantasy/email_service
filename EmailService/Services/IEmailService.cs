using System.Threading.Tasks;

namespace EmailService.Services
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string to, string subject, string body, string fromDisplayName = "Boom Bust");
    }
}
