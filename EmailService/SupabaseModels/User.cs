using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EmailService.SupabaseModels
{
    [Table("users", Schema = "auth")]
    public class User
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("aud")]
        public string? Aud { get; set; }

        [Column("role")]
        public string? Role { get; set; }

        [Column("email")]
        public string? Email { get; set; }

        [Column("email_confirmed_at")]
        public DateTime? EmailConfirmedAt { get; set; }

        [Column("invited_at")]
        public DateTime? InvitedAt { get; set; }

        [Column("confirmation_sent_at")]
        public DateTime? ConfirmationSentAt { get; set; }

        [Column("last_sign_in_at")]
        public DateTime? LastSignInAt { get; set; }

        [Column("app_metadata")]
        public string? AppMetadata { get; set; }

        [Column("user_metadata")]
        public string? UserMetadata { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
