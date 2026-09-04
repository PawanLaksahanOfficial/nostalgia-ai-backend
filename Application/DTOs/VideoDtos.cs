namespace Application.DTOs
{
    public class CreateVideoRequest
    {
        public string Title { get; set; } = string.Empty;
        public string StoryText { get; set; } = string.Empty;
        public string? MusicMood { get; set; }
    }

    public class UpdateVideoRequest
    {
        public string Title { get; set; } = string.Empty;
    }

    public class CreateShareLinkRequest
    {
        public int? ExpiresInDays { get; set; }
        public string? Label { get; set; }
    }

    public class ShareLinkDto
    {
        public int Id { get; set; }
        public string Token { get; set; } = string.Empty;
        public string ShareUrl { get; set; } = string.Empty;
        public string? Label { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsRevoked { get; set; }
        public bool IsExpired { get; set; }
        public int ViewCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PublicVideoDto
    {
        public string Token { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Narrative { get; set; }
        public double? DurationSeconds { get; set; }
        public string OwnerFirstName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int ViewCount { get; set; }
    }

    public enum ShareMediaKind
    {
        Video,
        Thumbnail
    }

    public class ShareMediaTarget
    {
        public int MemoryId { get; set; }
        public string StorageKey { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }

    // Pipeline DTOs -> passed between the worker and the composition services.
    // These are not wire contracts and are never serialized to clients.

    public class VideoCompositionRequest
    {
        public int MemoryId { get; set; }
        public string WorkingDirectory { get; set; } = string.Empty;
        public string? ImageFileName { get; set; }
        public string? VoiceoverFileName { get; set; }
        public string? MusicFileName { get; set; }
        public string? CaptionsFileName { get; set; }
        public string OutputFileName { get; set; } = "final.mp4";
        public string ThumbnailFileName { get; set; } = "thumb.jpg";
        public string FontFileName { get; set; } = "font.ttf";
        public double DurationSeconds { get; set; }
        public bool HighDefinition { get; set; }
        public bool AddWatermark { get; set; }
    }

    public class ComposedVideo
    {
        public string VideoFilePath { get; set; } = string.Empty;
        public string? ThumbnailFilePath { get; set; }
        public double DurationSeconds { get; set; }
        public long FileSizeBytes { get; set; }
        public string ContentType { get; set; } = "video/mp4";
    }

    public class SpeechResult
    {
        public string AudioFilePath { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public List<CaptionCue> Cues { get; set; } = new();
    }

    public class CaptionCue
    {
        public double StartSeconds { get; set; }
        public double EndSeconds { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public class MusicTrack
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }
}
