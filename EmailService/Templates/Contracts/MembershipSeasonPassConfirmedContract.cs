namespace EmailService.Templates.Contracts;

/// <summary>
/// Producer template `membership_season_pass_confirmed` — sent after a season
/// pass purchase activates. Added by boom FEAT-300 after PRD #17 closed, so it
/// is not in that ticket's template scope.
/// Required variables: tier, expires_at, checkin_credits_included.
/// </summary>
public class MembershipSeasonPassConfirmedContract : ITemplateContract
{
    public const string Key = "membership_season_pass_confirmed";

    public string TemplateKey => Key;
    public SenderIdentity? SenderOverride { get; init; }

    public required string RecipientEmail { get; init; }

    /// <summary>"Pro" or "MVP" as sent by the producer.</summary>
    public required string Tier { get; init; }

    public required string ExpiresAt { get; init; }
    public required string CheckinCreditsIncluded { get; init; }

    public static ITemplateContract Create(
        string recipientEmail, IReadOnlyDictionary<string, string> variables) =>
        new MembershipSeasonPassConfirmedContract
        {
            RecipientEmail = recipientEmail,
            Tier = TemplateVariables.Read(variables, "tier"),
            ExpiresAt = TemplateVariables.Read(variables, "expires_at"),
            CheckinCreditsIncluded = TemplateVariables.Read(variables, "checkin_credits_included")
        };

    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        TemplateVariables.RequireEmail(RecipientEmail, "recipient_email", errors);
        TemplateVariables.RequireText(Tier, "tier", errors);
        TemplateVariables.RequireText(ExpiresAt, "expires_at", errors);
        TemplateVariables.RequireText(CheckinCreditsIncluded, "checkin_credits_included", errors);

        return errors.Count == 0;
    }

    public Dictionary<string, string> ToBrevoParams() => new()
    {
        ["tier"] = Tier,
        ["expires_at"] = ExpiresAt,
        ["checkin_credits_included"] = CheckinCreditsIncluded
    };
}
