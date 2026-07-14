namespace Domain.Entities
{
    public enum VideoStatus
    {
        Pending,
        Processing,
        Completed,
        Failed
    }

    public enum VideoQuality
    {
        Standard,
        HD
    }

    public enum UserTier
    {
        Free,
        Premium
    }

    public class UserMemory
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public string Title { get; set; } = string.Empty;
        public string StoryText { get; set; } = string.Empty;
        public string? GeneratedNarrative { get; set; }
        public string? UserImagePath { get; set; }
        public string? MusicMood { get; set; }
        public string? GeneratedMusicPath { get; set; }
        public string? VoiceoverPath { get; set; }
        public string? FinalVideoPath { get; set; }
        public string? ThumbnailPath { get; set; }
        public string? ShareToken { get; set; }
        public DateTime? ShareExpiresAt { get; set; }
        public VideoStatus Status { get; set; } = VideoStatus.Pending;
        public VideoQuality Quality { get; set; } = VideoQuality.Standard;
        public bool IsPublic { get; set; }
        public int ViewCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public ICollection<Like> Likes { get; set; } = new List<Like>();
        public ICollection<Comment> Comments { get; set; } = new List<Comment>();
        public ICollection<Collection> Collections { get; set; } = new List<Collection>();
    }
}
