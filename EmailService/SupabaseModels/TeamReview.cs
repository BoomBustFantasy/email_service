using System;
using System.ComponentModel.DataAnnotations;
using EmailService.SupabaseModels.Enums;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace EmailService.SupabaseModels
{
    [Table("TeamReviews")]
    public class TeamReview : BaseModel
    {
        [PrimaryKey("id")]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("league_id")]
        public string LeagueId { get; set; } = string.Empty;

        [Required]
        [Column("status")]
        public AdviceStatus Status { get; set; }

        [Column("winning_team")]
        public int? WinningTeam { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("answered_at")]
        public DateTime? AnsweredAt { get; set; }

        [Required]
        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("reviewer_id")]
        public Guid? ReviewerId { get; set; }

        [Column("submitter_notes")]
        public string? SubmitterNotes { get; set; }

        [Column("reviewer_notes")]
        public string? ReviewerNotes { get; set; }

        [Column("email_sent")]
        public bool EmailSent { get; set; }

        [Column("youtube_link")]
        public string? YoutubeLink { get; set; }
    }
}
