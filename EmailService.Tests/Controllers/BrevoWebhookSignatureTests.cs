using EmailService.Controllers;
using FluentAssertions;
using Xunit;

namespace EmailService.Tests.Controllers;

/// <summary>
/// PRD #17 story 7: "webhook signature verification enforced, so spoofed Brevo
/// events cannot mutate state." These cover the accept/reject decision itself.
/// </summary>
public class BrevoWebhookSignatureTests
{
    private const string Secret = "test-webhook-secret";
    private const string Body = """{"event":"delivered","email":"user@example.com"}""";

    [Fact]
    public void ValidSignature_IsAccepted()
    {
        var signature = BrevoSignatureVerifier.ComputeSignature(Secret, Body);

        BrevoSignatureVerifier.IsValid(Secret, Body, signature).Should().BeTrue();
    }

    [Fact]
    public void UppercaseHexSignature_IsAccepted()
    {
        var signature = BrevoSignatureVerifier.ComputeSignature(Secret, Body).ToUpperInvariant();

        BrevoSignatureVerifier.IsValid(Secret, Body, signature).Should().BeTrue();
    }

    [Fact]
    public void TamperedBody_IsRejected()
    {
        var signature = BrevoSignatureVerifier.ComputeSignature(Secret, Body);
        var tampered = Body.Replace("user@example.com", "attacker@evil.com");

        BrevoSignatureVerifier.IsValid(Secret, tampered, signature).Should().BeFalse();
    }

    [Fact]
    public void WrongSecret_IsRejected()
    {
        var signature = BrevoSignatureVerifier.ComputeSignature("some-other-secret", Body);

        BrevoSignatureVerifier.IsValid(Secret, Body, signature).Should().BeFalse();
    }

    /// <summary>
    /// The regression this suite exists for: an unconfigured secret used to
    /// return true, so any unauthenticated POST could mutate delivery state.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingSecret_IsRejectedRatherThanSkipped(string? secret)
    {
        var signature = BrevoSignatureVerifier.ComputeSignature(Secret, Body);

        BrevoSignatureVerifier.IsValid(secret, Body, signature).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-hex-at-all")]
    [InlineData("abc")]          // odd length
    [InlineData("zz00")]         // non-hex characters
    public void MalformedSignature_IsRejected(string? signature)
    {
        BrevoSignatureVerifier.IsValid(Secret, Body, signature).Should().BeFalse();
    }

    [Fact]
    public void CorrectLengthButWrongSignature_IsRejected()
    {
        var wrong = new string('a', 64); // 32 bytes hex, same shape as a real HMAC

        BrevoSignatureVerifier.IsValid(Secret, Body, wrong).Should().BeFalse();
    }
}
