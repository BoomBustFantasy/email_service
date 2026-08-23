using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace EmailService.SupabaseModels;

[Table("email_outbox")]
public class EmailOutbox : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("idempotency_key")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Column("template_key")]
    public string TemplateKey { get; set; } = string.Empty;

    [Column("recipient_email")]
    public string RecipientEmail { get; set; } = string.Empty;

    [Column("template_variables")]
    public object? TemplateVariables { get; set; }

    [Column("classification")]
    public string Classification { get; set; } = "transactional";

    [Column("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [Column("published_at")]
    public DateTime? PublishedAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }
}
