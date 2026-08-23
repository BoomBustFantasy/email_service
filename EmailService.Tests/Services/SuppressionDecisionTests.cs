using EmailService.Services;
using EmailService.SupabaseModels;
using FluentAssertions;
using Xunit;

namespace EmailService.Tests.Services;

/// <summary>
/// PRD #17 stories 14 and 15: suppression is respected for marketing-classified
/// sends, while transactional messages continue to go out. Before this was wired
/// up, a single unsubscribe silently stopped a customer's review-completion mail.
/// </summary>
public class SuppressionDecisionTests
{
    [Fact]
    public void MarketingMessages_AreSubjectToSuppression()
    {
        QueueConsumerService.IsSuppressible("marketing").Should().BeTrue();
    }

    [Theory]
    [InlineData("Marketing")]
    [InlineData("MARKETING")]
    public void ClassificationMatching_IsCaseInsensitive(string classification)
    {
        QueueConsumerService.IsSuppressible(classification).Should().BeTrue();
    }

    [Fact]
    public void TransactionalMessages_BypassSuppression()
    {
        QueueConsumerService.IsSuppressible("transactional").Should().BeFalse();
    }

    /// <summary>
    /// Fail safe: an unset or unrecognised classification must not silently
    /// suppress a message that might be transactional.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("something-else")]
    public void UnknownOrMissingClassification_DoesNotSuppress(string? classification)
    {
        QueueConsumerService.IsSuppressible(classification).Should().BeFalse();
    }

    [Fact]
    public void OutboxRecords_DefaultToTransactional()
    {
        new EmailOutbox().Classification.Should().Be("transactional");
        QueueConsumerService.IsSuppressible(new EmailOutbox().Classification).Should().BeFalse();
    }
}
