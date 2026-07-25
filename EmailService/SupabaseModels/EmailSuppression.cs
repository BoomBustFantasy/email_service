using System.Text.Json.Serialization;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace EmailService.SupabaseModels;

/// <summary>
/// Tracks email addresses that have unsubscribed or been suppressed
/// </summary>
[Table("email_suppression")]
public class EmailSuppression : BaseModel
{
    [PrimaryKey("id", false)]
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [Column("email")]
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [Column("reason")]
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [Column("suppression_type")]
    [JsonPropertyName("suppression_type")]
    public string SuppressionType { get; set; } = "all";

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}
