namespace EmailService.Templates.Contracts;

/// <summary>
/// Producer template `trade_submitted` — confirmation to the submitter when a
/// trade enters the review queue. Added by boom FEAT-318 after PRD #17 closed,
/// so it is not in that ticket's template scope.
/// Required variables: trade_id, trade_url, next_show_label, youtube_url.
/// </summary>
public class TradeSubmittedContract : ITemplateContract
{
    public const string Key = "trade_submitted";

    public string TemplateKey => Key;
    public SenderIdentity? SenderOverride { get; init; }

    public required string RecipientEmail { get; init; }
    public required string TradeId { get; init; }
    public required string TradeUrl { get; init; }

    /// <summary>
    /// Pre-formatted by the producer, e.g. "Saturday, August 22 at 7:30 PM CT".
    /// Passed through as text — this service does not parse or reformat it.
    /// </summary>
    public required string NextShowLabel { get; init; }

    public required string YoutubeUrl { get; init; }

    public static ITemplateContract Create(
        string recipientEmail, IReadOnlyDictionary<string, string> variables) =>
        new TradeSubmittedContract
        {
            RecipientEmail = recipientEmail,
            TradeId = TemplateVariables.Read(variables, "trade_id"),
            TradeUrl = TemplateVariables.Read(variables, "trade_url"),
            NextShowLabel = TemplateVariables.Read(variables, "next_show_label"),
            YoutubeUrl = TemplateVariables.Read(variables, "youtube_url")
        };

    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        TemplateVariables.RequireEmail(RecipientEmail, "recipient_email", errors);
        TemplateVariables.RequireText(TradeId, "trade_id", errors);
        TemplateVariables.RequireUrl(TradeUrl, "trade_url", errors);
        TemplateVariables.RequireText(NextShowLabel, "next_show_label", errors);
        TemplateVariables.RequireUrl(YoutubeUrl, "youtube_url", errors);

        return errors.Count == 0;
    }

    public Dictionary<string, string> ToBrevoParams() => new()
    {
        ["trade_id"] = TradeId,
        ["trade_url"] = TradeUrl,
        ["next_show_label"] = NextShowLabel,
        ["youtube_url"] = YoutubeUrl
    };
}
