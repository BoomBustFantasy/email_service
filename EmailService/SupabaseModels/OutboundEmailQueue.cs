using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace EmailService.SupabaseModels;

[Table("OutboundEmailQueue")]
public class OutboundEmailQueue : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("idempotency_key")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Column("template_key")]
    public string TemplateKey { get; set; } = string.Empty;

    [Column("recipient_email")]
    public string RecipientEmail { get; set; } = string.Empty;

    [Column("template_variables")]
    public string TemplateVariables { get; set; } = string.Empty;

    [Column("status")]
    public string Status { get; set; } = "pending";

    [Column("retry_count")]
    public int RetryCount { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("locked_until")]
    public DateTime? LockedUntil { get; set; }

    [Column("last_error")]
    public string? LastError { get; set; }
}
