using EmailService.Templates;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EmailService.Tests.Templates;

public class TemplateContractRegistryTests
{
    private static readonly ITemplateContractRegistry Registry = new TemplateContractRegistry();

    private static readonly Dictionary<string, string> AnyVars = new()
    {
        ["trade_id"] = "1",
        ["trade_url"] = "https://boombustfantasy.com/trades/1",
        ["review_id"] = "1",
        ["review_url"] = "https://boombustfantasy.com/team-reviews/1",
        ["purchase_type"] = "credits",
        ["credits_amount"] = "10",
        ["recipient_name"] = "Jack"
    };

    [Theory]
    [InlineData("stripe_purchase_confirmation")]
    [InlineData("trade_review_completed")]
    [InlineData("team_review_ready")]
    [InlineData("reviewer_trade_assigned")]
    [InlineData("reviewer_team_assigned")]
    public void EveryPrdTemplateKey_HasARegisteredContract(string templateKey)
    {
        var created = Registry.TryCreate(
            templateKey, "user@example.com", AnyVars, out var contract, out var error);

        created.Should().BeTrue(because: $"PRD #17 requires '{templateKey}'; error was: {error}");
        contract.Should().NotBeNull();
        contract!.TemplateKey.Should().Be(templateKey);
    }

    /// <summary>
    /// An unknown key must not fall through to an unvalidated send — the whole
    /// point of routing the consumer through the registry.
    /// </summary>
    [Fact]
    public void UnknownTemplateKey_IsRejectedWithAHelpfulError()
    {
        var created = Registry.TryCreate(
            "not-a-real-template", "user@example.com", AnyVars, out var contract, out var error);

        created.Should().BeFalse();
        contract.Should().BeNull();
        error.Should().Contain("not-a-real-template").And.Contain("Known keys");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingTemplateKey_IsRejected(string templateKey)
    {
        Registry.TryCreate(templateKey, "user@example.com", AnyVars, out _, out var error)
            .Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TemplateKeyLookup_IsCaseInsensitive()
    {
        Registry.TryCreate("TEAM_REVIEW_READY", "user@example.com", AnyVars, out var contract, out _)
            .Should().BeTrue();
        contract!.TemplateKey.Should().Be("team_review_ready");
    }

    /// <summary>
    /// A key mapped to a Brevo ID but with no contract behind it would be sent
    /// unvalidated, which is exactly the gap this ticket had.
    /// </summary>
    [Fact]
    public void EveryConfiguredTemplateId_HasAContract()
    {
        var configuredKeys = new[]
        {
            "stripe_purchase_confirmation",
            "trade_review_completed",
            "team_review_ready",
            "reviewer_trade_assigned",
            "reviewer_team_assigned",
            "welcome_email"
        };

        Registry.RegisteredKeys.Should().Contain(configuredKeys);
    }
}

public class TemplateServiceTests
{
    private static ITemplateService Service(Dictionary<string, long> map) =>
        new TemplateService(Options.Create(new TemplateConfig
        {
            TemplateIdMap = map,
            DefaultSender = new SenderIdentity { Email = "hello@boombustfantasy.com", Name = "Boom Bust Fantasy" }
        }));

    [Fact]
    public void ResolveTemplateId_ReturnsTheConfiguredId()
    {
        Service(new() { ["team_review_ready"] = 4 })
            .ResolveTemplateId("team_review_ready").Should().Be(4);
    }

    [Fact]
    public void ResolveTemplateId_UnconfiguredKey_Throws()
    {
        var act = () => Service(new()).ResolveTemplateId("team_review_ready");

        act.Should().Throw<KeyNotFoundException>().WithMessage("*team_review_ready*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveTemplateId_BlankKey_Throws(string key)
    {
        var act = () => Service(new()).ResolveTemplateId(key);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetSenderIdentity_FallsBackToTheDefaultSender()
    {
        var contract = EmailService.Templates.Contracts.TeamReviewReadyContract.Create(
            "user@example.com",
            new Dictionary<string, string>
            {
                ["review_id"] = "1",
                ["review_url"] = "https://boombustfantasy.com/team-reviews/1"
            });

        Service(new()).GetSenderIdentity(contract).Email.Should().Be("hello@boombustfantasy.com");
    }
}
