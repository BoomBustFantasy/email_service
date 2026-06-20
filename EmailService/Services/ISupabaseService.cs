using System.Collections.Generic;
using System.Threading.Tasks;
using EmailService.DTOs;

namespace EmailService.Services
{
    public interface ISupabaseService
    {
        Task<List<TradeEmailInfo>> GetCompletedTradeReviewsWithUsersAsync();
        Task MarkEmailSentAsync(long tradeId);
        Task<List<TeamReviewEmailInfo>> GetPendingTeamReviewEmailsWithYoutubeAsync();
        Task MarkTeamReviewEmailedAsync(long teamReviewId);
        Task<List<(long Id, string Email)>> GetTradesForReviewerNotificationAsync();
        Task MarkReviewerNotifiedAsync(long tradeId);
        Task<List<TeamReviewNotificationInfo>> GetTeamReviewsForReviewerNotificationAsync();
        Task MarkTeamReviewerNotifiedAsync(long teamReviewId);
    }
}
