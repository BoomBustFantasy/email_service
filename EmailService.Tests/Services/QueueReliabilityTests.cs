using EmailService.Services;
using FluentAssertions;
using Xunit;

namespace EmailService.Tests.Services;

/// <summary>
/// The give-up rule. pgmq's read_ct counts deliveries, so it is 1 on the first
/// attempt — an off-by-one here either dead-letters healthy mail on its first
/// try or reproduces the July 2026 incident where a message was retried 217
/// times because nothing ever stopped.
/// </summary>
public class RetryExhaustionTests
{
    [Theory]
    [InlineData(1, false)]  // first delivery
    [InlineData(2, false)]  // second
    [InlineData(3, true)]   // third exhausts the default budget
    [InlineData(4, true)]
    [InlineData(217, true)] // the July message
    public void ExhaustsAfterConfiguredAttempts(int readCount, bool shouldDeadLetter)
    {
        QueueConsumerService.HasExhaustedRetries(readCount, maxRetryAttempts: 3)
            .Should().Be(shouldDeadLetter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveBudget_NeverDeadLetters(int maxRetryAttempts)
    {
        QueueConsumerService.HasExhaustedRetries(999, maxRetryAttempts).Should().BeFalse();
    }
}

/// <summary>
/// The staleness gate. A backlog built up over a consumer outage is full of
/// notifications that are no longer true — in August 2026 the queue held
/// trade-review-complete mail up to 18 days old.
/// </summary>
public class MessageStalenessTests
{
    private const int MaxAgeHours = 48;

    [Fact]
    public void FreshMessage_IsNotStale()
    {
        QueueConsumerService.IsStale(DateTime.UtcNow.AddHours(-1), MaxAgeHours, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void JustInsideTheWindow_IsNotStale()
    {
        QueueConsumerService.IsStale(DateTime.UtcNow.AddHours(-47), MaxAgeHours, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void PastTheWindow_IsStale()
    {
        QueueConsumerService.IsStale(DateTime.UtcNow.AddHours(-49), MaxAgeHours, out var age)
            .Should().BeTrue();

        age.TotalHours.Should().BeApproximately(49, 0.5);
    }

    [Fact]
    public void EighteenDayBacklog_IsStale()
    {
        QueueConsumerService.IsStale(DateTime.UtcNow.AddDays(-18), MaxAgeHours, out _)
            .Should().BeTrue();
    }

    /// <summary>
    /// Postgres timestamps can arrive with Unspecified kind; treating them as
    /// local time would shift staleness by the server's UTC offset.
    /// </summary>
    [Fact]
    public void UnspecifiedKind_IsTreatedAsUtc()
    {
        var justInside = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-1), DateTimeKind.Unspecified);

        QueueConsumerService.IsStale(justInside, MaxAgeHours, out _).Should().BeFalse();
    }

    /// <summary>Fail safe: never drop mail because a timestamp is missing.</summary>
    [Fact]
    public void MissingTimestamp_IsNotStale()
    {
        QueueConsumerService.IsStale(null, MaxAgeHours, out _).Should().BeFalse();
    }

    [Fact]
    public void DisabledGate_NeverDropsAnything()
    {
        QueueConsumerService.IsStale(DateTime.UtcNow.AddYears(-1), maxAgeHours: 0, out _)
            .Should().BeFalse();
    }
}

public class QueueMetricsTests
{
    [Fact]
    public void SuccessRate_IsOneBeforeAnythingIsProcessed()
    {
        new QueueMetrics().SuccessRate.Should().Be(1.0);
    }

    [Fact]
    public void SuccessRate_ReflectsOutcomes()
    {
        var metrics = new QueueMetrics();

        for (var i = 0; i < 4; i++) metrics.IncrementProcessed();
        metrics.IncrementSuccessful();
        metrics.IncrementSuccessful();
        metrics.IncrementSuccessful();
        metrics.IncrementFailed();

        metrics.SuccessRate.Should().Be(0.75);
    }

    [Fact]
    public void DeadLetterDepth_IsTrackedAndSnapshotted()
    {
        var metrics = new QueueMetrics();
        metrics.SetDeadLetterDepth(7);
        metrics.IncrementDeadLettered();

        metrics.DeadLetterDepth.Should().Be(7);
        metrics.GetSnapshot()["dead_letter_depth"].Should().Be(7L);
        metrics.GetSnapshot()["total_dead_lettered"].Should().Be(1L);
    }

    [Fact]
    public void CountersAreThreadSafe()
    {
        var metrics = new QueueMetrics();

        Parallel.For(0, 1000, _ => metrics.IncrementProcessed());

        metrics.TotalProcessed.Should().Be(1000);
    }
}
