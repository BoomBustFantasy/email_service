using EmailService.Templates.Contracts;

namespace EmailService.Templates;

/// <summary>
/// Builds a typed contract from a producer's raw template payload.
/// </summary>
public interface ITemplateContractRegistry
{
    /// <summary>
    /// Constructs the contract registered for <paramref name="templateKey"/>.
    /// Returns false with a reason when the key has no registered contract, so the
    /// consumer can dead-letter the message instead of sending an unvalidated email.
    /// </summary>
    bool TryCreate(
        string templateKey,
        string recipientEmail,
        IReadOnlyDictionary<string, string> variables,
        out ITemplateContract? contract,
        out string? error);

    /// <summary>Template keys that have a registered contract.</summary>
    IReadOnlyCollection<string> RegisteredKeys { get; }
}

public class TemplateContractRegistry : ITemplateContractRegistry
{
    private delegate ITemplateContract ContractFactory(
        string recipientEmail, IReadOnlyDictionary<string, string> variables);

    private static readonly Dictionary<string, ContractFactory> Factories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // PRD #17 template scope
            [StripePurchaseConfirmationContract.Key] = StripePurchaseConfirmationContract.Create,
            [TradeReviewCompletedContract.Key] = TradeReviewCompletedContract.Create,
            [TeamReviewReadyContract.Key] = TeamReviewReadyContract.Create,
            [ReviewerTeamAssignedContract.Key] = ReviewerTeamAssignedContract.Create,

            // ReviewerTradeAssignedContract is intentionally absent. The key exists
            // in boom's EmailTemplateKey union but has no call site and has never
            // produced a message (re-confirmed against the producer 2026-08-24:
            // zero call sites in server/ or app/, zero email_outbox rows ever), so
            // there is no Brevo template to map it to.
            //
            // The trap: boom still exports it in EmailTemplateKey with a full typed
            // variable contract (trade_id, trade_url), and its emailProducers.test.ts
            // asserts both that contract and a 'trade-assigned-{id}' idempotency key.
            // A developer wiring it up therefore gets green types and a green test
            // suite on the producer side while every message is rejected as invalid
            // and archived here, with nothing failing anywhere to say so.
            //
            // If the producer ever wires it, register it here and add its ID to
            // TemplateIdMap — the startup validator enforces that both are done.

            // Added to the producer after PRD #17 closed. Not in that ticket's
            // template scope, but live traffic depends on them.
            [WelcomeEmailContract.Key] = WelcomeEmailContract.Create,
            [TradeSubmittedContract.Key] = TradeSubmittedContract.Create,
            [MembershipSeasonPassConfirmedContract.Key] = MembershipSeasonPassConfirmedContract.Create
        };

    public IReadOnlyCollection<string> RegisteredKeys => Factories.Keys;

    public bool TryCreate(
        string templateKey,
        string recipientEmail,
        IReadOnlyDictionary<string, string> variables,
        out ITemplateContract? contract,
        out string? error)
    {
        contract = null;
        error = null;

        if (string.IsNullOrWhiteSpace(templateKey))
        {
            error = "Template key is missing";
            return false;
        }

        if (!Factories.TryGetValue(templateKey, out var factory))
        {
            error = $"Template key '{templateKey}' has no registered contract. " +
                    $"Known keys: {string.Join(", ", Factories.Keys.Order())}";
            return false;
        }

        contract = factory(recipientEmail, variables);
        return true;
    }
}
