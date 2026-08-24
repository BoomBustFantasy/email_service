namespace EmailService.Services;

/// <summary>
/// The only values <c>email_delivery_log.status</c> accepts.
///
/// The column carries a CHECK constraint — <c>valid_status</c>, defined in boom's
/// <c>20260725165628_email_foundation.sql</c> — permitting exactly these six.
/// Anything else fails the insert, and because <c>LogDeliveryAttempt</c> swallows
/// its own exceptions the row is simply lost while the queue message is archived
/// regardless. The failure is completely silent.
///
/// This service wrote "stale" and "invalid" until 2026-08-24, so every message
/// dropped for staleness or rejected for a bad payload was discarded with no
/// record of why. Verified against prod that day: only 'failed', 'sent' and
/// 'pending' rows have ever existed.
///
/// <para>
/// <b>Known remaining break.</b> <c>BrevoWebhookController.MapEventToStatus</c>
/// still returns "delivered", "opened", "clicked", "unsubscribed", "spam" and
/// "blocked", none of which are permitted. Those need the constraint widened
/// rather than remapped, since they are genuine distinct states. Note the
/// knock-on: the idempotency check treats "delivered" as already-processed, but
/// no row can ever hold that value, so only "sent" does any work.
/// </para>
/// </summary>
public static class DeliveryStatus
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Bounced = "bounced";
    public const string Rejected = "rejected";
    public const string Deferred = "deferred";

    /// <summary>
    /// Prefix on the <c>error_message</c> of a row rejected for staleness, so the
    /// two rejection causes stay distinguishable while they share a status.
    /// </summary>
    public const string StaleReasonPrefix = "Stale: ";

    /// <summary>
    /// Prefix on the <c>error_message</c> of a row rejected for a bad payload.
    /// </summary>
    public const string InvalidReasonPrefix = "Invalid: ";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Pending, Sent, Failed, Bounced, Rejected, Deferred
        };
}
