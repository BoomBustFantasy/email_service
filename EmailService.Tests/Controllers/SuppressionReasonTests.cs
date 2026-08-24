using EmailService.Controllers;
using EmailService.Services;
using FluentAssertions;
using Xunit;

namespace EmailService.Tests.Controllers;

/// <summary>
/// email_suppression.reason is CHECK-constrained. The webhook mapped Brevo's
/// event names straight through until 2026-08-24 and five of the six values it
/// produced were illegal, so every bounce and spam complaint threw on insert,
/// returned 500 to Brevo, and retried into the same failure. Only an unsubscribe
/// could ever suppress an address — prod held zero suppression rows.
/// </summary>
public class SuppressionReasonTests
{
    /// <summary>
    /// Mirrors valid_suppression_reason on public.email_suppression, read from
    /// prod on 2026-08-24:
    ///
    ///   CHECK (reason = ANY (ARRAY['unsubscribe','bounce','complaint','manual']))
    ///
    /// If the constraint changes, change this in the same commit.
    /// </summary>
    private static readonly string[] ConstraintAllows =
        ["unsubscribe", "bounce", "complaint", "manual"];

    [Fact]
    public void AllowedSet_MatchesTheDatabaseConstraintExactly()
    {
        SuppressionReason.Allowed.Should().BeEquivalentTo(ConstraintAllows);
    }

    [Theory]
    [InlineData("unsubscribed", "unsubscribe")]
    [InlineData("hard_bounce", "bounce")]
    [InlineData("soft_bounce", "bounce")]
    [InlineData("invalid_email", "bounce")]
    [InlineData("blocked", "bounce")]
    [InlineData("complaint", "complaint")]
    public void EverySuppressionEvent_MapsToAPermittedReason(string brevoEvent, string expected)
    {
        var reason = BrevoWebhookController.MapEventToSuppressionReason(brevoEvent);

        reason.Should().Be(expected);
        ConstraintAllows.Should().Contain(reason!);
    }

    [Fact]
    public void EveryEventTreatedAsSuppression_HasAMapping()
    {
        // These are exactly the events IsSuppressionEvent routes here. Any one of
        // them returning null would silently stop suppressing that failure mode.
        var suppressionEvents = new[]
        {
            "hard_bounce", "soft_bounce", "invalid_email", "unsubscribed", "complaint", "blocked"
        };

        foreach (var name in suppressionEvents)
        {
            BrevoWebhookController.MapEventToSuppressionReason(name)
                .Should().NotBeNull($"'{name}' is routed to suppression handling");
        }
    }

    [Theory]
    [InlineData("delivered")]
    [InlineData("opened")]
    [InlineData("click")]
    [InlineData("request")]
    [InlineData("")]
    public void NonSuppressionEvents_MapToNullRatherThanAnIllegalValue(string brevoEvent)
    {
        BrevoWebhookController.MapEventToSuppressionReason(brevoEvent).Should().BeNull();
    }

    [Theory]
    [InlineData("HARD_BOUNCE")]
    [InlineData("Unsubscribed")]
    public void EventMatching_IsCaseInsensitive(string brevoEvent)
    {
        BrevoWebhookController.MapEventToSuppressionReason(brevoEvent).Should().NotBeNull();
    }

    [Theory]
    [InlineData("hard_bounce")]
    [InlineData("soft_bounce")]
    [InlineData("invalid")]
    [InlineData("spam")]
    [InlineData("blocked")]
    [InlineData("unknown")]
    public void TheOldReasonValues_WouldStillBeRejectedByTheConstraint(string old)
    {
        ConstraintAllows.Should().NotContain(old);
    }

    [Fact]
    public void SuppressionTypeUsedOnInsert_IsPermitted()
    {
        var constraintAllows = new[] { "all", "marketing", "transactional" };

        constraintAllows.Should().Contain(SuppressionType.All);
        SuppressionType.Allowed.Should().BeEquivalentTo(constraintAllows);
    }
}
