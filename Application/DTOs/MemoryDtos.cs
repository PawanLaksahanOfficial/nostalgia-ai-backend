using Domain.Entities;

namespace Application.DTOs
{
    public class ShareMemoryRequest
    {
        public bool MakePublic { get; set; } = true;
        public int? ExpirationDays { get; set; }
    }

    public class MemoryShareResponse
    {
        public int MemoryId { get; set; }
        public string ShareToken { get; set; } = string.Empty;
        public string ShareUrl { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
        public bool IsPublic { get; set; }
    }

    public class SharedMemoryResponse
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string StoryText { get; set; } = string.Empty;
        public string? FinalVideoPath { get; set; }
        public string? ThumbnailPath { get; set; }
        public VideoQuality Quality { get; set; }
        public int ViewCount { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class RegenerateMemoryRequest
    {
        public string? NewStoryText { get; set; }
        public string? NewMusicMood { get; set; }
    }
}