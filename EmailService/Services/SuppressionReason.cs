namespace EmailService.Services;

/// <summary>
/// The only values <c>email_suppression.reason</c> accepts, per the
/// <c>valid_suppression_reason</c> CHECK constraint.
///
/// The webhook mapped Brevo's event names straight through until 2026-08-24 —
/// "hard_bounce", "soft_bounce", "invalid", "spam", "blocked" — and five of the
/// six were illegal. Unlike the delivery log this failed loudly: the insert
/// throws, the endpoint returns 500, and Brevo retries into the same failure
/// forever. Only an unsubscribe could ever suppress an address.
/// </summary>
public static class SuppressionReason
{
    public const string Unsubscribe = "unsubscribe";
    public const string Bounce = "bounce";
    public const string Complaint = "complaint";
    public const string Manual = "manual";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.Ordinal) { Unsubscribe, Bounce, Complaint, Manual };
}

/// <summary>
/// The only values <c>email_suppression.suppression_type</c> accepts, per the
/// <c>valid_suppression_type</c> CHECK constraint.
/// </summary>
public static class SuppressionType
{
    public const string All = "all";
    public const string Marketing = "marketing";
    public const string Transactional = "transactional";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.Ordinal) { All, Marketing, Transactional };
}
