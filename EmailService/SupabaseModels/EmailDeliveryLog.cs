using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace EmailService.SupabaseModels;

[Table("email_delivery_log")]
public class EmailDeliveryLog : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("outbox_id")]
    public Guid? OutboxId { get; set; }

    [Column("idempotency_key")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Column("attempt_number")]
    public int AttemptNumber { get; set; } = 1;

    [Column("status")]
    public string Status { get; set; } = "pending";

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("external_id")]
    public string? ExternalId { get; set; }

    [Column("attempted_at")]
    public DateTime? AttemptedAt { get; set; }
}
