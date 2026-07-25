using EmailService.DTOs;

namespace EmailService.Templates.Contracts;

/// <summary>
/// Template contract for team review notification emails
/// </summary>
public class TeamReviewNotificationContract : ITemplateContract
{
    public string TemplateKey => "team-review-notification";
    public SenderIdentity? SenderOverride { get; init; }
    
    // Required template variables
    public required string RecipientEmail { get; init; }
    public required string RecipientName { get; init; }
    public required string ReviewerName { get; init; }
    public required string TeamName { get; init; }
    public required string LeagueName { get; init; }
    public required string ReviewUrl { get; init; }
    
    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(RecipientEmail))
            errors.Add("RecipientEmail is required");
        
        if (string.IsNullOrWhiteSpace(RecipientName))
            errors.Add("RecipientName is required");
        
        if (string.IsNullOrWhiteSpace(ReviewerName))
            errors.Add("ReviewerName is required");
        
        if (string.IsNullOrWhiteSpace(TeamName))
            errors.Add("TeamName is required");
        
        if (string.IsNullOrWhiteSpace(LeagueName))
            errors.Add("LeagueName is required");
        
        if (string.IsNullOrWhiteSpace(ReviewUrl))
            errors.Add("ReviewUrl is required");
        
        // Validate email format
        if (!string.IsNullOrWhiteSpace(RecipientEmail) && !IsValidEmail(RecipientEmail))
            errors.Add($"RecipientEmail '{RecipientEmail}' is not a valid email address");
        
        return errors.Count == 0;
    }
    
    public Dictionary<string, string> ToBrevoParams()
    {
        return new Dictionary<string, string>
        {
            ["recipient_name"] = RecipientName,
            ["reviewer_name"] = ReviewerName,
            ["team_name"] = TeamName,
            ["league_name"] = LeagueName,
            ["review_url"] = ReviewUrl
        };
    }
    
    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
