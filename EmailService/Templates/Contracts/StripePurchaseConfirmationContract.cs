namespace EmailService.Templates.Contracts;

/// <summary>
/// PRD #17 template `stripe_purchase_confirmation`.
/// Required variables: product_type, credits_amount.
/// PRD #17 and boom #247 both specify the name `purchase_type`, but the
/// shipped producer sends `product_type` and the queue payloads confirm it.
/// The producer wins - see docs/email-templates.md.
/// </summary>
public class StripePurchaseConfirmationContract : ITemplateContract
{
    public const string Key = "stripe_purchase_confirmation";

    public string TemplateKey => Key;
    public SenderIdentity? SenderOverride { get; init; }

    public required string RecipientEmail { get; init; }
    public required string ProductType { get; init; }
    public required string CreditsAmount { get; init; }

    public static ITemplateContract Create(
        string recipientEmail, IReadOnlyDictionary<string, string> variables) =>
        new StripePurchaseConfirmationContract
        {
            RecipientEmail = recipientEmail,
            ProductType = TemplateVariables.Read(variables, "product_type"),
            CreditsAmount = TemplateVariables.Read(variables, "credits_amount")
        };

    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        TemplateVariables.RequireEmail(RecipientEmail, "recipient_email", errors);
        TemplateVariables.RequireText(ProductType, "product_type", errors);
        TemplateVariables.RequireText(CreditsAmount, "credits_amount", errors);

        return errors.Count == 0;
    }

    public Dictionary<string, string> ToBrevoParams() => new()
    {
        ["product_type"] = ProductType,
        ["credits_amount"] = CreditsAmount
    };
}
