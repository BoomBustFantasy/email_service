using System.Text.Json.Serialization;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace EmailService.SupabaseModels;

/// <summary>
/// Dead letter queue for emails that failed after all retry attempts
/// </summary>
[Table("DeadLetterEmail")]
public class DeadLetterEmail : BaseModel
{
    [PrimaryKey("id", false)]
    [JsonPropertyName("id")]
    public long Id { get; set; }
    
    [Column("original_queue_id")]
    [JsonPropertyName("original_queue_id")]
    public long? OriginalQueueId { get; set; }
    
    [Column("recipient_email")]
    [JsonPropertyName("recipient_email")]
    public string RecipientEmail { get; set; } = string.Empty;
    
    [Column("template_key")]
    [JsonPropertyName("template_key")]
    public string TemplateKey { get; set; } = string.Empty;
    
    [Column("template_params")]
    [JsonPropertyName("template_params")]
    public string TemplateParams { get; set; } = string.Empty;
    
    [Column("attempts")]
    [JsonPropertyName("attempts")]
    public int Attempts { get; set; }
    
    [Column("last_error")]
    [JsonPropertyName("last_error")]
    public string? LastError { get; set; }
    
    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
    
    [Column("moved_to_dlq_at")]
    [JsonPropertyName("moved_to_dlq_at")]
    public DateTime MovedToDlqAt { get; set; }
}
