namespace EmailService.Templates.Contracts;

/// <summary>
/// Template contract for purchase confirmation emails
/// </summary>
public class PurchaseConfirmationContract : ITemplateContract
{
    public string TemplateKey => "purchase-confirmation";
    public SenderIdentity? SenderOverride { get; init; }

    // Required template variables
    public required string RecipientEmail { get; init; }
    public required string RecipientName { get; init; }
    public required string ProductName { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string TransactionId { get; init; }
    public required DateTime PurchaseDate { get; init; }

    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(RecipientEmail))
            errors.Add("RecipientEmail is required");

        if (string.IsNullOrWhiteSpace(RecipientName))
            errors.Add("RecipientName is required");

        if (string.IsNullOrWhiteSpace(ProductName))
            errors.Add("ProductName is required");

        if (Amount <= 0)
            errors.Add("Amount must be greater than zero");

        if (string.IsNullOrWhiteSpace(Currency))
            errors.Add("Currency is required");

        if (string.IsNullOrWhiteSpace(TransactionId))
            errors.Add("TransactionId is required");

        if (PurchaseDate == default)
            errors.Add("PurchaseDate is required");

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
            ["product_name"] = ProductName,
            ["amount"] = Amount.ToString("F2"),
            ["currency"] = Currency,
            ["transaction_id"] = TransactionId,
            ["purchase_date"] = PurchaseDate.ToString("MMMM dd, yyyy")
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
