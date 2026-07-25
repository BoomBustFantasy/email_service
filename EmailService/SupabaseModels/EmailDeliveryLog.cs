using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace EmailService.SupabaseModels;

[Table("EmailDeliveryLog")]
public class EmailDeliveryLog : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("idempotency_key")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Column("queue_message_id")]
    public long? QueueMessageId { get; set; }

    [Column("template_key")]
    public string TemplateKey { get; set; } = string.Empty;

    [Column("brevo_template_id")]
    public long? BrevoTemplateId { get; set; }

    [Column("recipient_email")]
    public string RecipientEmail { get; set; } = string.Empty;

    [Column("template_variables")]
    public string TemplateVariables { get; set; } = string.Empty;

    [Column("status")]
    public string Status { get; set; } = "pending";

    [Column("brevo_message_id")]
    public string? BrevoMessageId { get; set; }

    [Column("sent_at")]
    public DateTime? SentAt { get; set; }

    [Column("delivered_at")]
    public DateTime? DeliveredAt { get; set; }

    [Column("bounced_at")]
    public DateTime? BouncedAt { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
