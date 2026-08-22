namespace EmailService.Templates.Contracts;

/// <summary>
/// Shared shape for the four PRD #17 review templates, each of which carries a
/// record id and a link to it. Subclasses supply the template key and the two
/// variable names the Brevo template expects.
/// </summary>
public abstract class ReviewLinkContract : ITemplateContract
{
    public abstract string TemplateKey { get; }

    /// <summary>Variable name for the record id, e.g. `trade_id` or `review_id`.</summary>
    protected abstract string IdVariable { get; }

    /// <summary>Variable name for the link, e.g. `trade_url` or `review_url`.</summary>
    protected abstract string UrlVariable { get; }

    public SenderIdentity? SenderOverride { get; init; }

    public required string RecipientEmail { get; init; }
    public required string RecordId { get; init; }
    public required string RecordUrl { get; init; }

    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        TemplateVariables.RequireEmail(RecipientEmail, "recipient_email", errors);
        TemplateVariables.RequireText(RecordId, IdVariable, errors);
        TemplateVariables.RequireUrl(RecordUrl, UrlVariable, errors);

        return errors.Count == 0;
    }

    public Dictionary<string, string> ToBrevoParams() => new()
    {
        [IdVariable] = RecordId,
        [UrlVariable] = RecordUrl
    };

    /// <summary>
    /// Reads the id and url variables a subclass declares out of the producer payload.
    /// </summary>
    protected static (string Id, string Url) ReadVariables(
        IReadOnlyDictionary<string, string> variables, string idVariable, string urlVariable) =>
        (TemplateVariables.Read(variables, idVariable), TemplateVariables.Read(variables, urlVariable));
}

/// <summary>PRD #17 template `trade_review_completed`. Variables: trade_id, trade_url.</summary>
public sealed class TradeReviewCompletedContract : ReviewLinkContract
{
    public const string Key = "trade_review_completed";
    public const string IdVar = "trade_id";
    public const string UrlVar = "trade_url";

    public override string TemplateKey => Key;
    protected override string IdVariable => IdVar;
    protected override string UrlVariable => UrlVar;

    public static ITemplateContract Create(
        string recipientEmail, IReadOnlyDictionary<string, string> variables)
    {
        var (id, url) = ReadVariables(variables, IdVar, UrlVar);
        return new TradeReviewCompletedContract
        {
            RecipientEmail = recipientEmail,
            RecordId = id,
            RecordUrl = url
        };
    }
}

/// <summary>PRD #17 template `reviewer_trade_assigned`. Variables: trade_id, trade_url.</summary>
public sealed class ReviewerTradeAssignedContract : ReviewLinkContract
{
    public const string Key = "reviewer_trade_assigned";
    public const string IdVar = "trade_id";
    public const string UrlVar = "trade_url";

    public override string TemplateKey => Key;
    protected override string IdVariable => IdVar;
    protected override string UrlVariable => UrlVar;

    public static ITemplateContract Create(
        string recipientEmail, IReadOnlyDictionary<string, string> variables)
    {
        var (id, url) = ReadVariables(variables, IdVar, UrlVar);
        return new ReviewerTradeAssignedContract
        {
            RecipientEmail = recipientEmail,
            RecordId = id,
            RecordUrl = url
        };
    }
}

/// <summary>PRD #17 template `team_review_ready`. Variables: review_id, review_url.</summary>
public sealed class TeamReviewReadyContract : ReviewLinkContract
{
    public const string Key = "team_review_ready";
    public const string IdVar = "review_id";
    public const string UrlVar = "review_url";

    public override string TemplateKey => Key;
    protected override string IdVariable => IdVar;
    protected override string UrlVariable => UrlVar;

    public static ITemplateContract Create(
        string recipientEmail, IReadOnlyDictionary<string, string> variables)
    {
        var (id, url) = ReadVariables(variables, IdVar, UrlVar);
        return new TeamReviewReadyContract
        {
            RecipientEmail = recipientEmail,
            RecordId = id,
            RecordUrl = url
        };
    }
}

/// <summary>PRD #17 template `reviewer_team_assigned`. Variables: review_id, review_url.</summary>
public sealed class ReviewerTeamAssignedContract : ReviewLinkContract
{
    public const string Key = "reviewer_team_assigned";
    public const string IdVar = "review_id";
    public const string UrlVar = "review_url";

    public override string TemplateKey => Key;
    protected override string IdVariable => IdVar;
    protected override string UrlVariable => UrlVar;

    public static ITemplateContract Create(
        string recipientEmail, IReadOnlyDictionary<string, string> variables)
    {
        var (id, url) = ReadVariables(variables, IdVar, UrlVar);
        return new ReviewerTeamAssignedContract
        {
            RecipientEmail = recipientEmail,
            RecordId = id,
            RecordUrl = url
        };
    }
}
