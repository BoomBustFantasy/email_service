using EmailService.Services;
using FluentAssertions;
using Xunit;

namespace EmailService.Tests.Services;

/// <summary>
/// email_delivery_log.status is constrained in the database. Writing a value
/// outside the permitted set fails the insert, and LogDeliveryAttempt swallows
/// the exception, so the row disappears while the queue message is archived
/// anyway — a completely silent loss of the reason a message was not sent.
///
/// The consumer wrote "stale" and "invalid" until 2026-08-24. Neither is
/// permitted, so neither was ever recorded.
/// </summary>
public class DeliveryStatusTests
{
    /// <summary>
    /// Mirrors valid_status on public.email_delivery_log, read from prod on
    /// 2026-08-24:
    ///
    ///   CHECK (status = ANY (ARRAY['pending','sent','failed','bounced','rejected','deferred']))
    ///
    /// If the constraint is widened in boom's migrations, update this list in the
    /// same change — the point of the test is that the two cannot drift quietly.
    /// </summary>
    private static readonly string[] ConstraintAllows =
        ["pending", "sent", "failed", "bounced", "rejected", "deferred"];

    [Fact]
    public void AllowedSet_MatchesTheDatabaseConstraintExactly()
    {
        DeliveryStatus.Allowed.Should().BeEquivalentTo(ConstraintAllows);
    }

    [Theory]
    [InlineData(DeliveryStatus.Sent)]
    [InlineData(DeliveryStatus.Failed)]
    [InlineData(DeliveryStatus.Rejected)]
    [InlineData(DeliveryStatus.Pending)]
    public void EveryStatusTheConsumerWrites_IsAcceptedByTheConstraint(string status)
    {
        ConstraintAllows.Should().Contain(status);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("invalid")]
    public void TheOldStatusValues_WouldStillBeRejectedByTheConstraint(string status)
    {
        // Guards against anyone reintroducing them as literals.
        ConstraintAllows.Should().NotContain(status);
        DeliveryStatus.Allowed.Should().NotContain(status);
    }

    [Fact]
    public void StaleAndInvalidRejections_StayDistinguishableByReasonPrefix()
    {
        // Both are permanent, deliberate non-sends recorded as 'rejected', so the
        // error_message prefix is the only thing separating them.
        DeliveryStatus.StaleReasonPrefix.Should().NotBeNullOrWhiteSpace();
        DeliveryStatus.InvalidReasonPrefix.Should().NotBeNullOrWhiteSpace();
        DeliveryStatus.StaleReasonPrefix.Should().NotBe(DeliveryStatus.InvalidReasonPrefix);
    }
}
