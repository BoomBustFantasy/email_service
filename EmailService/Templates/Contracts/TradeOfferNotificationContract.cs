using EmailService.DTOs;

namespace EmailService.Templates.Contracts;

/// <summary>
/// Template contract for trade offer notification emails
/// </summary>
public class TradeOfferNotificationContract : ITemplateContract
{
    public string TemplateKey => "trade-offer-notification";
    public SenderIdentity? SenderOverride { get; init; }

    // Required template variables
    public required string RecipientEmail { get; init; }
    public required string RecipientName { get; init; }
    public required string SenderTeamName { get; init; }
    public required string ReceiverTeamName { get; init; }
    public required string LeagueName { get; init; }
    public required string TradeUrl { get; init; }
    public required string SentPlayers { get; init; }
    public required string ReceivedPlayers { get; init; }

    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(RecipientEmail))
            errors.Add("RecipientEmail is required");

        if (string.IsNullOrWhiteSpace(RecipientName))
            errors.Add("RecipientName is required");

        if (string.IsNullOrWhiteSpace(SenderTeamName))
            errors.Add("SenderTeamName is required");

        if (string.IsNullOrWhiteSpace(ReceiverTeamName))
            errors.Add("ReceiverTeamName is required");

        if (string.IsNullOrWhiteSpace(LeagueName))
            errors.Add("LeagueName is required");

        if (string.IsNullOrWhiteSpace(TradeUrl))
            errors.Add("TradeUrl is required");

        if (string.IsNullOrWhiteSpace(SentPlayers))
            errors.Add("SentPlayers is required");

        if (string.IsNullOrWhiteSpace(ReceivedPlayers))
            errors.Add("ReceivedPlayers is required");

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
            ["sender_team_name"] = SenderTeamName,
            ["receiver_team_name"] = ReceiverTeamName,
            ["league_name"] = LeagueName,
            ["trade_url"] = TradeUrl,
            ["sent_players"] = SentPlayers,
            ["received_players"] = ReceivedPlayers
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
