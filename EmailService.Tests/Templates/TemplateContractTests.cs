using EmailService.Templates;
using EmailService.Templates.Contracts;
using FluentAssertions;
using Xunit;

namespace EmailService.Tests.Templates;

/// <summary>
/// PRD #17 story 3: "strict template contract validation before sending, so
/// malformed messages fail fast and visibly." One class per PRD template key.
/// </summary>
public class TemplateContractTests
{
    private const string Recipient = "user@example.com";

    private static Dictionary<string, string> Vars(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    // --- the five PRD template keys ------------------------------------------

    [Fact]
    public void PrdTemplateKeys_AreExactlyWhatTheTicketSpecifies()
    {
        var keys = new[]
        {
            StripePurchaseConfirmationContract.Key,
            TradeReviewCompletedContract.Key,
            TeamReviewReadyContract.Key,
            ReviewerTradeAssignedContract.Key,
            ReviewerTeamAssignedContract.Key
        };

        keys.Should().BeEquivalentTo(new[]
        {
            "stripe_purchase_confirmation",
            "trade_review_completed",
            "team_review_ready",
            "reviewer_trade_assigned",
            "reviewer_team_assigned"
        });
    }

    // --- stripe_purchase_confirmation ----------------------------------------

    [Fact]
    public void StripePurchaseConfirmation_ValidPayload_Passes()
    {
        var contract = StripePurchaseConfirmationContract.Create(
            Recipient, Vars(("purchase_type", "credits"), ("credits_amount", "50")));

        contract.Validate(out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        contract.ToBrevoParams().Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["purchase_type"] = "credits",
            ["credits_amount"] = "50"
        });
    }

    [Fact]
    public void StripePurchaseConfirmation_MissingCreditsAmount_FailsAndNamesTheVariable()
    {
        var contract = StripePurchaseConfirmationContract.Create(
            Recipient, Vars(("purchase_type", "credits")));

        contract.Validate(out var errors).Should().BeFalse();
        errors.Should().ContainSingle().Which.Should().Contain("credits_amount");
    }

    // --- the four review templates -------------------------------------------

    public static TheoryData<string, string, string, Func<string, IReadOnlyDictionary<string, string>, ITemplateContract>>
        ReviewContracts => new()
    {
        { "trade_review_completed", "trade_id", "trade_url", TradeReviewCompletedContract.Create },
        { "reviewer_trade_assigned", "trade_id", "trade_url", ReviewerTradeAssignedContract.Create },
        { "team_review_ready", "review_id", "review_url", TeamReviewReadyContract.Create },
        { "reviewer_team_assigned", "review_id", "review_url", ReviewerTeamAssignedContract.Create }
    };

    [Theory]
    [MemberData(nameof(ReviewContracts))]
    public void ReviewContract_ValidPayload_PassesAndEmitsPrdVariableNames(
        string expectedKey,
        string idVar,
        string urlVar,
        Func<string, IReadOnlyDictionary<string, string>, ITemplateContract> create)
    {
        var contract = create(Recipient, Vars(
            (idVar, "1234"), (urlVar, "https://boombustfantasy.com/x/1234")));

        contract.TemplateKey.Should().Be(expectedKey);
        contract.Validate(out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        contract.ToBrevoParams().Keys.Should().BeEquivalentTo(new[] { idVar, urlVar });
    }

    [Theory]
    [MemberData(nameof(ReviewContracts))]
    public void ReviewContract_MissingBothVariables_ReportsBothErrors(
        string expectedKey,
        string idVar,
        string urlVar,
        Func<string, IReadOnlyDictionary<string, string>, ITemplateContract> create)
    {
        _ = expectedKey;
        var contract = create(Recipient, Vars());

        contract.Validate(out var errors).Should().BeFalse();
        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.Contains(idVar));
        errors.Should().Contain(e => e.Contains(urlVar));
    }

    [Theory]
    [MemberData(nameof(ReviewContracts))]
    public void ReviewContract_NonHttpUrl_IsRejected(
        string expectedKey,
        string idVar,
        string urlVar,
        Func<string, IReadOnlyDictionary<string, string>, ITemplateContract> create)
    {
        _ = expectedKey;
        var contract = create(Recipient, Vars((idVar, "1234"), (urlVar, "javascript:alert(1)")));

        contract.Validate(out var errors).Should().BeFalse();
        errors.Should().Contain(e => e.Contains(urlVar));
    }

    [Theory]
    [MemberData(nameof(ReviewContracts))]
    public void ReviewContract_InvalidRecipientEmail_IsRejected(
        string expectedKey,
        string idVar,
        string urlVar,
        Func<string, IReadOnlyDictionary<string, string>, ITemplateContract> create)
    {
        _ = expectedKey;
        var contract = create("not-an-email", Vars(
            (idVar, "1234"), (urlVar, "https://boombustfantasy.com/x/1234")));

        contract.Validate(out var errors).Should().BeFalse();
        errors.Should().Contain(e => e.Contains("recipient_email"));
    }

    [Fact]
    public void ReviewContract_RecipientEmailIsExposedOnTheInterface()
    {
        ITemplateContract contract = TeamReviewReadyContract.Create(
            Recipient, Vars(("review_id", "7"), ("review_url", "https://boombustfantasy.com/team-reviews/7")));

        contract.RecipientEmail.Should().Be(Recipient);
    }
}
