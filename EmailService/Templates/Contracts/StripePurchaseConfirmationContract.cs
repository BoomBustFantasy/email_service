namespace EmailService.Templates.Contracts;

/// <summary>
/// PRD #17 template `stripe_purchase_confirmation`.
/// Required variables: purchase_type, credits_amount.
/// </summary>
public class StripePurchaseConfirmationContract : ITemplateContract
{
    public const string Key = "stripe_purchase_confirmation";

    public string TemplateKey => Key;
    public SenderIdentity? SenderOverride { get; init; }

    public required string RecipientEmail { get; init; }
    public required string PurchaseType { get; init; }
    public required string CreditsAmount { get; init; }

    public static ITemplateContract Create(
        string recipientEmail, IReadOnlyDictionary<string, string> variables) =>
        new StripePurchaseConfirmationContract
        {
            RecipientEmail = recipientEmail,
            PurchaseType = TemplateVariables.Read(variables, "purchase_type"),
            CreditsAmount = TemplateVariables.Read(variables, "credits_amount")
        };

    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        TemplateVariables.RequireEmail(RecipientEmail, "recipient_email", errors);
        TemplateVariables.RequireText(PurchaseType, "purchase_type", errors);
        TemplateVariables.RequireText(CreditsAmount, "credits_amount", errors);

        return errors.Count == 0;
    }

    public Dictionary<string, string> ToBrevoParams() => new()
    {
        ["purchase_type"] = PurchaseType,
        ["credits_amount"] = CreditsAmount
    };
}
