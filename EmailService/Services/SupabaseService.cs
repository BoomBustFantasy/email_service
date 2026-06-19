using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EmailService.Configs;
using Microsoft.Extensions.Options;

namespace EmailService.Services
{
    public class TradeEmailInfo
    {
        public long TradeId { get; set; }
        public string UserEmail { get; set; } = string.Empty;
    }

    public interface ISupabaseService
    {
        Task<List<TradeEmailInfo>> GetCompletedTradeReviewsWithUsersAsync();
        Task MarkEmailSentAsync(long tradeId);
        Task<List<EmailService.SupabaseModels.TeamReview>> GetPendingTeamReviewsWithYoutubeAsync();
        Task<List<EmailService.DTOs.TeamReviewEmailInfo>> GetPendingTeamReviewEmailsWithYoutubeAsync();
        Task<EmailService.SupabaseModels.User?> GetUserByIdAsync(System.Guid userId);
        Task MarkTeamReviewEmailedAsync(long teamReviewId);
        Task<List<(long Id, string Email)>> GetTradesForReviewerNotificationAsync();
        Task MarkReviewerNotifiedAsync(long tradeId);
    }

    public class SupabaseService : ISupabaseService
    {
        private readonly SupabaseConfig _config;
        private readonly HttpClient _httpClient;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };

        public SupabaseService(IOptions<SupabaseConfig> config)
        {
            _config = config.Value;
            _httpClient = new HttpClient();
        }

        public async Task<List<TradeEmailInfo>> GetCompletedTradeReviewsWithUsersAsync()
        {
            var url = $"{_config.Url}/rest/v1/rpc/get_trades_for_email";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ServiceRoleKey);
            request.Headers.Add("apikey", _config.ServiceRoleKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new System.Exception($"Supabase error: {response.StatusCode} - {json}");
            }

            var trades = JsonSerializer.Deserialize<List<TradeEmailInfo>>(json, _jsonOptions) ?? new List<TradeEmailInfo>();
            return trades;
        }

        public async Task MarkEmailSentAsync(long tradeId)
        {
            await RetryAsync(async () =>
            {
                var url = $"{_config.Url}/rest/v1/Trades?id=eq.{tradeId}";
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ServiceRoleKey);
                request.Headers.Add("apikey", _config.ServiceRoleKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent("[{\"email_sent\": true}]", System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new System.Exception($"Supabase error: {response.StatusCode} - {json}");
                }
            });
        }

        public async Task<List<EmailService.SupabaseModels.TeamReview>> GetPendingTeamReviewsWithYoutubeAsync()
        {
            var url = $"{_config.Url}/rest/v1/rpc/get_team_reviews_with_youtube";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ServiceRoleKey);
            request.Headers.Add("apikey", _config.ServiceRoleKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new System.Exception($"Supabase error: {response.StatusCode} - {json}");
            }

            var reviews = JsonSerializer.Deserialize<List<EmailService.SupabaseModels.TeamReview>>(json, _jsonOptions) ?? new List<EmailService.SupabaseModels.TeamReview>();
            return reviews;
        }

        public async Task<List<EmailService.DTOs.TeamReviewEmailInfo>> GetPendingTeamReviewEmailsWithYoutubeAsync()
        {
            var url = $"{_config.Url}/rest/v1/rpc/get_team_review_emails_with_youtube";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ServiceRoleKey);
            request.Headers.Add("apikey", _config.ServiceRoleKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new System.Exception($"Supabase error: {response.StatusCode} - {json}");
            }

            var reviews = JsonSerializer.Deserialize<List<EmailService.DTOs.TeamReviewEmailInfo>>(json, _jsonOptions) ?? new List<EmailService.DTOs.TeamReviewEmailInfo>();
            return reviews;
        }

        public async Task<EmailService.SupabaseModels.User?> GetUserByIdAsync(System.Guid userId)
        {
            var url = $"{_config.Url}/rest/v1/auth.users?id=eq.{userId}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ServiceRoleKey);
            request.Headers.Add("apikey", _config.ServiceRoleKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new System.Exception($"Supabase error: {response.StatusCode} - {json}");
            }

            var users = JsonSerializer.Deserialize<List<EmailService.SupabaseModels.User>>(json, _jsonOptions);
            return users != null && users.Count > 0 ? users[0] : null;
        }

        public async Task MarkTeamReviewEmailedAsync(long teamReviewId)
        {
            await RetryAsync(async () =>
            {
                var url = $"{_config.Url}/rest/v1/TeamReviews?id=eq.{teamReviewId}";
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ServiceRoleKey);
                request.Headers.Add("apikey", _config.ServiceRoleKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent("[{\"email_sent\": true}]", System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new System.Exception($"Supabase error: {response.StatusCode} - {json}");
                }
            });
        }

        public async Task<List<(long Id, string Email)>> GetTradesForReviewerNotificationAsync()
        {
            var url = $"{_config.Url}/rest/v1/rpc/get_trades_for_reviewer_notification";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ServiceRoleKey);
            request.Headers.Add("apikey", _config.ServiceRoleKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new System.Exception($"Supabase error: {response.StatusCode} - {json}");
            }

            var trades = JsonSerializer.Deserialize<List<TradeReviewerNotificationDto>>(json, _jsonOptions) ?? new List<TradeReviewerNotificationDto>();
            var result = new List<(long, string)>();
            foreach (var t in trades)
            {
                result.Add((t.Id, t.Email));
            }
            return result;
        }

        public async Task MarkReviewerNotifiedAsync(long tradeId)
        {
            await RetryAsync(async () =>
            {
                var url = $"{_config.Url}/rest/v1/Trades?id=eq.{tradeId}";
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ServiceRoleKey);
                request.Headers.Add("apikey", _config.ServiceRoleKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent("[{\"reviewer_notified\": true}]", System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new System.Exception($"Supabase error: {response.StatusCode} - {json}");
                }
            });
        }

        private static async Task RetryAsync(Func<Task> action, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try { await action(); return; }
                catch when (attempt < maxRetries)
                { await Task.Delay(1000 * attempt); }
            }
        }

        private class TradeReviewerNotificationDto
        {
            public long Id { get; set; }
            public string Email { get; set; } = string.Empty;
        }
    }
}
