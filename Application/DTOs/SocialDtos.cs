namespace Application.DTOs
{
    public class LikeResponse
    {
        public int MemoryId { get; set; }
        public int LikeCount { get; set; }
        public bool IsLikedByCurrentUser { get; set; }
    }

    public class CommentRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    public class CommentResponse
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class CreateCollectionRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class UpdateCollectionRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    public class CollectionResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int MemoryCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AddMemoryToCollectionRequest
    {
        public int MemoryId { get; set; }
    }

    public class GiftMemoryRequest
    {
        public int MemoryId { get; set; }
        public string RecipientEmail { get; set; } = string.Empty;
        public string? Message { get; set; }
    }

    public class GiftResponse
    {
        public int Id { get; set; }
        public int MemoryId { get; set; }
        public string MemoryTitle { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string RecipientEmail { get; set; } = string.Empty;
        public string? Message { get; set; }
        public bool IsOpened { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
