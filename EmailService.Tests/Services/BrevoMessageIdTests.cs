using EmailService.Services;
using FluentAssertions;
using Xunit;

namespace EmailService.Tests.Services;

/// <summary>
/// Brevo's message ID is the only provider-side proof a send was accepted, and
/// BrevoWebhookController matches delivery webhooks against it through
/// EmailDeliveryLog.ExternalId. It was never persisted until 2026-08-24: all 16
/// rows that ever reached 'sent' carry a null external_id, so none can be
/// reconciled and no delivery or bounce webhook could find its row.
///
/// Parsing must never throw. Brevo has already accepted the send by this point,
/// so an unreadable body costs reconciliation, not the email.
/// </summary>
public class BrevoMessageIdTests
{
    [Fact]
    public void SingleRecipientResponse_YieldsTheMessageId()
    {
        BrevoEmailService.ReadMessageId("""{"messageId":"<202608241200.1@smtp-relay.brevo.com>"}""")
            .Should().Be("<202608241200.1@smtp-relay.brevo.com>");
    }

    [Fact]
    public void BatchResponse_YieldsTheFirstMessageId()
    {
        BrevoEmailService.ReadMessageId("""{"messageIds":["<a@brevo.com>","<b@brevo.com>"]}""")
            .Should().Be("<a@brevo.com>");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"messageId":""}""")]
    [InlineData("""{"messageId":null}""")]
    [InlineData("""{"messageIds":[]}""")]
    [InlineData("""{"messageId":12345}""")]
    public void UnusableResponse_YieldsNullInsteadOfThrowing(string body)
    {
        var act = () => BrevoEmailService.ReadMessageId(body);

        act.Should().NotThrow();
        act().Should().BeNull();
    }

    [Fact]
    public void FailedResult_CarriesNoMessageId()
    {
        var result = SendResult.Failed();

        result.Success.Should().BeFalse();
        result.MessageId.Should().BeNull();
    }

    [Fact]
    public void SentResult_CarriesTheMessageId()
    {
        var result = SendResult.Sent("<x@brevo.com>");

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("<x@brevo.com>");
    }
}
