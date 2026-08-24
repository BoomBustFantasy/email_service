namespace EmailService.Services;

/// <summary>
/// Outcome of a template send, carrying Brevo's message ID on success.
///
/// The ID is the only provider-side proof a send was accepted, and
/// <see cref="Controllers.BrevoWebhookController"/> matches delivery webhooks
/// against it via <c>EmailDeliveryLog.ExternalId</c>. It went unrecorded until
/// 2026-08-24, so every <c>sent</c> row had a null <c>external_id</c>: nothing
/// could be reconciled against Brevo, and no delivery or bounce webhook could
/// ever find the row it belonged to.
/// </summary>
public readonly record struct SendResult(bool Success, string? MessageId)
{
    public static SendResult Failed() => new(false, null);

    public static SendResult Sent(string? messageId) => new(true, messageId);
}
