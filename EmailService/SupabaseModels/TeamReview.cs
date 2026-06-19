using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EmailService.SupabaseModels
{
    [Table("TeamReviews", Schema = "public")]
    public class TeamReview
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("league_id")]
        public string LeagueId { get; set; } = string.Empty;

        [Required]
        [Column("status")]
        public int Status { get; set; }

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
