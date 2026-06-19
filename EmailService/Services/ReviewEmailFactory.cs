using EmailService.Configs;
using EmailService.DTOs;
using Microsoft.Extensions.Options;

namespace EmailService.Services;

public class ReviewEmailFactory
{
    private readonly string _baseUrl;

    public ReviewEmailFactory(IOptions<AppConfig> config)
    {
        _baseUrl = config.Value.BaseUrl.TrimEnd('/');
    }

    public EmailMessage BuildTradeReviewCompleted(long tradeId) => new(
        Subject: "Your Trade Review Has Been Completed",
        Body: $"Hello,\n\nA trade of yours has been reviewed. You can view it here: {_baseUrl}/trades/{tradeId}\n\nThank you for using Boom Bust!"
    );

    public EmailMessage BuildTeamReviewReady(string youtubeLink) => new(
        Subject: "Your Team Review is Ready!",
        Body: $"Hello,\n\nYour team review is ready. You can watch it here: {youtubeLink}\n\nThank you for using Boom Bust!"
    );

    public EmailMessage BuildReviewerNotification(long tradeId) => new(
        Subject: "You have a trade to review!",
        Body: $"Hello,\n\nYou have been assigned to review trade #{tradeId}. You can view it here: {_baseUrl}/trades/{tradeId}\n\nThank you!"
    );
}
