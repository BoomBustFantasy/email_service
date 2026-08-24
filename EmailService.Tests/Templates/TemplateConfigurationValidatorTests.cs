using EmailService.Templates;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EmailService.Tests.Templates;

/// <summary>
/// The validator exists so template misconfiguration fails at boot instead of
/// per-message. Both failure shapes below actually happened in July 2026: an
/// unmapped key retried one message 217 times, and a mapped key with no contract
/// silently dropped mail.
/// </summary>
public class TemplateConfigurationValidatorTests
{
    private static TemplateConfig Config(params (string Key, long Id)[] entries) =>
        new()
        {
            TemplateIdMap = entries.ToDictionary(e => e.Key, e => e.Id),
            DefaultSender = new SenderIdentity { Email = "hello@boombustfantasy.com", Name = "Boom Bust Fantasy" }
        };

    private sealed class FakeRegistry(params string[] keys) : ITemplateContractRegistry
    {
        public IReadOnlyCollection<string> RegisteredKeys { get; } = keys;

        public bool TryCreate(
            string templateKey, string recipientEmail, IReadOnlyDictionary<string, string> variables,
            out ITemplateContract? contract, out string? error)
        {
            contract = null;
            error = null;
            return false;
        }
    }

    [Fact]
    public void FullyWiredConfiguration_Passes()
    {
        var act = () => TemplateConfigurationValidator.Validate(
            new FakeRegistry("a", "b"), Config(("a", 1), ("b", 2)));

        act.Should().NotThrow();
    }

    [Fact]
    public void ContractWithNoTemplateId_FailsStartup()
    {
        var act = () => TemplateConfigurationValidator.Validate(
            new FakeRegistry("a", "b"), Config(("a", 1)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'b' has a contract but no entry in TemplateIdMap*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PlaceholderTemplateId_FailsStartup(long templateId)
    {
        var act = () => TemplateConfigurationValidator.Validate(
            new FakeRegistry("a"), Config(("a", templateId)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a real template*");
    }

    [Fact]
    public void TemplateIdWithNoContract_FailsStartup()
    {
        var act = () => TemplateConfigurationValidator.Validate(
            new FakeRegistry("a"), Config(("a", 1), ("orphan", 9)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'orphan' has a Brevo template ID but no registered contract*");
    }

    [Fact]
    public void AllProblems_AreReportedTogether()
    {
        var act = () => TemplateConfigurationValidator.Validate(
            new FakeRegistry("a", "b"), Config(("a", 0), ("orphan", 9)));

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;

        message.Should().Contain("'a'").And.Contain("'b'").And.Contain("'orphan'");
    }

    /// <summary>
    /// Guards the real shipped configuration: every contract has an ID and every
    /// ID has a contract. This is the test that would have caught trade_submitted
    /// being produced with no consumer-side contract.
    /// </summary>
    [Fact]
    public void ShippedConfiguration_IsFullyWired()
    {
        var shipped = Config(
            ("reviewer_team_assigned", 5),
            ("welcome_email", 6),
            ("trade_submitted", 7),
            ("trade_review_completed", 8),
            ("team_review_ready", 9),
            ("stripe_purchase_confirmation", 10),
            ("membership_season_pass_confirmed", 11));

        var act = () => TemplateConfigurationValidator.Validate(new TemplateContractRegistry(), shipped);

        act.Should().NotThrow();
    }

    /// <summary>
    /// The template ID map has to be inside the container image to have any
    /// effect. It used to live only in appsettings.json, which is gitignored and
    /// therefore absent from the Docker build context, so production booted with
    /// an empty map and every registered key failed the validator at once. This
    /// test binds the file that actually ships, so a key added to the registry
    /// without an ID in appsettings.Defaults.json fails the build rather than
    /// the deploy.
    /// </summary>
    [Fact]
    public void ShippedDefaultsFile_CoversEveryRegisteredContract()
    {
        var shipped = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Defaults.json", optional: false)
            .Build()
            .GetSection("Templates")
            .Get<TemplateConfig>();

        shipped.Should().NotBeNull("appsettings.Defaults.json must contain a Templates section");

        var act = () => TemplateConfigurationValidator.Validate(new TemplateContractRegistry(), shipped!);

        act.Should().NotThrow();
    }
}
