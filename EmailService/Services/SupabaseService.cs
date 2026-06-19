using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmailService.Configs;
using EmailService.DTOs;
using EmailService.SupabaseModels;
using Microsoft.Extensions.Options;
using Supabase;
using Supabase.Gotrue.Interfaces;
using static Supabase.Postgrest.Constants;

namespace EmailService.Services
{
    public class SupabaseService : ISupabaseService
    {
        private readonly Client _supabase;
        private readonly IGotrueAdminClient<Supabase.Gotrue.User> _adminAuth;

        public SupabaseService(Client supabase, IOptions<SupabaseConfig> config)
        {
            _supabase = supabase;

            if (string.IsNullOrEmpty(config.Value.ServiceRoleKey))
            {
                throw new InvalidOperationException("Supabase service role key is missing");
            }

            _adminAuth = _supabase.AdminAuth(config.Value.ServiceRoleKey);
        }

        public async Task<List<TradeEmailInfo>> GetCompletedTradeReviewsWithUsersAsync()
        {
            var trades = await _supabase
                .From<TradeReview>()
                .Filter("status", Operator.Equals, "complete")
                .Where(trade => trade.EmailSent == false)
                .Select("id,user_id")
                .Get();

            var result = new List<TradeEmailInfo>();

            foreach (var trade in trades.Models)
            {
                var user = await _adminAuth.GetUserById(trade.UserId.ToString());

                if (!string.IsNullOrWhiteSpace(user?.Email))
                {
                    result.Add(new TradeEmailInfo
                    {
                        TradeId = trade.Id,
                        UserEmail = user.Email
                    });
                }
            }

            return result;
        }

        public async Task MarkEmailSentAsync(long tradeId)
        {
            await _supabase
                .From<TradeReview>()
                .Where(trade => trade.Id == tradeId)
                .Set(trade => trade.EmailSent, true)
                .Update();
        }

        public async Task<List<TeamReviewEmailInfo>> GetPendingTeamReviewEmailsWithYoutubeAsync()
        {
            var reviews = await _supabase
                .From<TeamReview>()
                .Where(review => review.EmailSent == false)
                .Where(review => review.YoutubeLink != null)
                .Select("id,user_id,youtube_link")
                .Get();

            var result = new List<TeamReviewEmailInfo>();

            foreach (var review in reviews.Models)
            {
                var user = await _adminAuth.GetUserById(review.UserId.ToString());

                if (!string.IsNullOrWhiteSpace(user?.Email))
                {
                    result.Add(new TeamReviewEmailInfo
                    {
                        Id = review.Id,
                        Email = user.Email,
                        YoutubeLink = review.YoutubeLink
                    });
                }
            }

            return result;
        }

        public async Task MarkTeamReviewEmailedAsync(long teamReviewId)
        {
            await _supabase
                .From<TeamReview>()
                .Where(review => review.Id == teamReviewId)
                .Set(review => review.EmailSent, true)
                .Update();
        }

        public async Task<List<(long Id, string Email)>> GetTradesForReviewerNotificationAsync()
        {
            var trades = await _supabase
                .From<TradeReview>()
                .Where(trade => trade.ReviewerNotified == false)
                .Where(trade => trade.ReviewerId != null)
                .Select("id,reviewer_id")
                .Get();

            var result = new List<(long Id, string Email)>();

            foreach (var trade in trades.Models.Where(trade => trade.ReviewerId.HasValue))
            {
                var user = await _adminAuth.GetUserById(trade.ReviewerId.Value.ToString());

                if (!string.IsNullOrWhiteSpace(user?.Email))
                {
                    result.Add((trade.Id, user.Email));
                }
            }

            return result;
        }

        public async Task MarkReviewerNotifiedAsync(long tradeId)
        {
            await _supabase
                .From<TradeReview>()
                .Where(trade => trade.Id == tradeId)
                .Set(trade => trade.ReviewerNotified, true)
                .Update();
        }
    }
}
