using System.Text.Json.Serialization;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace EmailService.SupabaseModels;

/// <summary>
/// Tracks email addresses that have unsubscribed or been suppressed
/// </summary>
[Table("EmailSuppression")]
public class EmailSuppression : BaseModel
{
    [PrimaryKey("id", false)]
    [JsonPropertyName("id")]
    public long Id { get; set; }
    
    [Column("email")]
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    
    [Column("reason")]
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
    
    [Column("suppressed_at")]
    [JsonPropertyName("suppressed_at")]
    public DateTime SuppressedAt { get; set; }
    
    [Column("details")]
    [JsonPropertyName("details")]
    public string? Details { get; set; }
}
