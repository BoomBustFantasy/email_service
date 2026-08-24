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
            Recipient, Vars(("product_type", "credits"), ("credits_amount", "50")));

        contract.Validate(out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        contract.ToBrevoParams().Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["product_type"] = "credits",
            ["credits_amount"] = "50"
        });
    }

    [Fact]
    public void StripePurchaseConfirmation_MissingCreditsAmount_FailsAndNamesTheVariable()
    {
        var contract = StripePurchaseConfirmationContract.Create(
            Recipient, Vars(("product_type", "credits")));

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

    // --- templates the producer added after PRD #17 closed --------------------

    private static Dictionary<string, string> TradeSubmittedVars() => new()
    {
        ["trade_id"] = "1467",
        ["trade_url"] = "https://boombustfantasy.com/trades/1467",
        ["next_show_label"] = "Saturday, August 22 at 7:30 PM CT",
        ["youtube_url"] = "https://www.youtube.com/@BoomBustFantasy"
    };

    [Fact]
    public void TradeSubmitted_RealProducerPayload_Passes()
    {
        var contract = TradeSubmittedContract.Create(Recipient, TradeSubmittedVars());

        contract.Validate(out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        contract.ToBrevoParams().Should().BeEquivalentTo(TradeSubmittedVars());
    }

    [Theory]
    [InlineData("trade_id")]
    [InlineData("trade_url")]
    [InlineData("next_show_label")]
    [InlineData("youtube_url")]
    public void TradeSubmitted_MissingAnyVariable_FailsAndNamesIt(string missing)
    {
        var vars = TradeSubmittedVars();
        vars.Remove(missing);

        TradeSubmittedContract.Create(Recipient, vars).Validate(out var errors).Should().BeFalse();
        errors.Should().Contain(e => e.Contains(missing));
    }

    /// <summary>next_show_label is a formatted human string, not a date.</summary>
    [Fact]
    public void TradeSubmitted_PassesNextShowLabelThroughVerbatim()
    {
        var contract = TradeSubmittedContract.Create(Recipient, TradeSubmittedVars());

        contract.ToBrevoParams()["next_show_label"]
            .Should().Be("Saturday, August 22 at 7:30 PM CT");
    }

    private static Dictionary<string, string> SeasonPassVars() => new()
    {
        ["tier"] = "Pro",
        ["expires_at"] = "2027-02-01",
        ["checkin_credits_included"] = "6"
    };

    [Fact]
    public void MembershipSeasonPassConfirmed_ValidPayload_Passes()
    {
        var contract = MembershipSeasonPassConfirmedContract.Create(Recipient, SeasonPassVars());

        contract.Validate(out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        contract.ToBrevoParams().Should().BeEquivalentTo(SeasonPassVars());
    }

    [Theory]
    [InlineData("tier")]
    [InlineData("expires_at")]
    [InlineData("checkin_credits_included")]
    public void MembershipSeasonPassConfirmed_MissingAnyVariable_FailsAndNamesIt(string missing)
    {
        var vars = SeasonPassVars();
        vars.Remove(missing);

        MembershipSeasonPassConfirmedContract.Create(Recipient, vars)
            .Validate(out var errors).Should().BeFalse();
        errors.Should().Contain(e => e.Contains(missing));
    }

    // --- welcome_email --------------------------------------------------------
    //
    // The producer enqueues welcome_email with an empty variable object; its type
    // in boom is literally Record<string, never>. RecipientName was required until
    // 2026-08-24, so every welcome email failed Validate(), was archived as
    // 'invalid', and was never retried — silent permanent loss, untested on both
    // sides. These pin the empty payload as the supported shape.

    [Fact]
    public void WelcomeEmail_WithNoVariablesAtAll_IsValid()
    {
        var contract = WelcomeEmailContract.Create(Recipient, Vars());

        contract.Validate(out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void WelcomeEmail_WithNoVariables_SendsNoBrevoParams()
    {
        // Brevo template 6 must not depend on {{params.recipient_name}} — there is
        // nothing to render it with until the producer starts sending a name.
        WelcomeEmailContract.Create(Recipient, Vars())
            .ToBrevoParams().Should().BeEmpty();
    }

    [Fact]
    public void WelcomeEmail_StillForwardsOptionalVariablesWhenPresent()
    {
        var contract = WelcomeEmailContract.Create(Recipient, Vars(
            ("recipient_name", "Jack"),
            ("get_started_url", "https://boombustfantasy.com/start")));

        contract.Validate(out _).Should().BeTrue();
        contract.ToBrevoParams().Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["recipient_name"] = "Jack",
            ["get_started_url"] = "https://boombustfantasy.com/start"
        });
    }

    [Fact]
    public void WelcomeEmail_StillRejectsAMalformedRecipient()
    {
        WelcomeEmailContract.Create("not-an-email", Vars())
            .Validate(out var errors).Should().BeFalse();
        errors.Should().Contain(e => e.Contains("not a valid email address"));
    }
}
