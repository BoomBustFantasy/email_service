using System.Text.Json.Serialization;

namespace EmailService.SupabaseModels.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdviceStatus
{
    Pending,
    InProgress,
    Complete
}
